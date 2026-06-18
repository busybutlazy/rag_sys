import uuid
import time
from arango.exceptions import IndexCreateError
from app.embedder import DIMENSIONS

KNOWLEDGE_GRAPH_NAME = "notebook_knowledge_graph"

# Edge definitions for the opt-in entity/fact graph layer (Phase 19). Inert
# until something writes mentions/facts into it — see
# docs/superpowers/plans/phase-19-graphrag-foundations.md.
_GRAPH_EDGE_DEFINITIONS = [
    {
        "edge_collection": "chunk_mentions_entity",
        "from_vertex_collections": ["chunks"],
        "to_vertex_collections": ["entities"],
    },
    {
        "edge_collection": "fact_has_participant",
        "from_vertex_collections": ["facts"],
        "to_vertex_collections": ["entities"],
    },
    {
        "edge_collection": "fact_supported_by_chunk",
        "from_vertex_collections": ["facts"],
        "to_vertex_collections": ["chunks"],
    },
]


def ensure_collections(db) -> None:
    for name in ["documents", "chunks", "notebooks", "experiments", "entities", "facts"]:
        if not db.has_collection(name):
            db.create_collection(name)
    for edge_def in _GRAPH_EDGE_DEFINITIONS:
        name = edge_def["edge_collection"]
        if not db.has_collection(name):
            db.create_collection(name, edge=True)


def ensure_knowledge_graph(db) -> None:
    if not db.has_graph(KNOWLEDGE_GRAPH_NAME):
        db.create_graph(KNOWLEDGE_GRAPH_NAME, edge_definitions=_GRAPH_EDGE_DEFINITIONS)


def ensure_graph_indexes(db) -> None:
    fields = ["notebook_id", "retrieval_version_id"]
    for name in ("entities", "facts"):
        col = db.collection(name)
        existing = {tuple(idx.get("fields", [])) for idx in col.indexes()}
        if tuple(fields) not in existing:
            col.add_index({"type": "persistent", "fields": fields})


def ensure_entities_view(db) -> None:
    properties = {
        "links": {
            "entities": {
                "fields": {
                    "canonical_name": {"analyzers": ["text_en"]},
                    "aliases": {"analyzers": ["text_en"]},
                    "notebook_id": {"analyzers": ["identity"]},
                    "user_id": {"analyzers": ["identity"]},
                    "retrieval_version_id": {"analyzers": ["identity"]},
                }
            }
        }
    }
    existing = {v["name"] for v in db.views()}
    if "entities_view" not in existing:
        db.create_view(
            name="entities_view",
            view_type="arangosearch",
            properties=properties,
        )
    else:
        db.update_arangosearch_view(name="entities_view", properties=properties)


def ensure_vector_index(db) -> None:
    col = db.collection("chunks")
    existing = {idx["type"] for idx in col.indexes()}
    if "vector" not in existing:
        document_count = col.count()
        if document_count == 0:
            return

        # ArangoDB trains vector indexes from existing embeddings. nLists must
        # not exceed the number of documents available for training.
        n_lists = min(2, document_count)
        for attempt in range(60):
            try:
                col.add_index({
                    "type": "vector",
                    "fields": ["embedding"],
                    "params": {
                        "metric": "cosine",
                        "dimension": DIMENSIONS,
                        "nLists": n_lists,
                    },
                })
                return
            except IndexCreateError as exc:
                if "vector index not ready" not in str(exc) or attempt == 59:
                    raise
                time.sleep(10)


def ensure_search_view(db) -> None:
    properties = {
        "links": {
            "chunks": {
                "fields": {
                    "text": {"analyzers": ["text_en"]},
                    "notebook_id": {"analyzers": ["identity"]},
                    "user_id": {"analyzers": ["identity"]},
                    "source_id": {"analyzers": ["identity"]},
                    "retrieval_version_id": {"analyzers": ["identity"]},
                    "chunk_index": {},
                }
            }
        }
    }
    existing = {v["name"] for v in db.views()}
    if "chunks_view" not in existing:
        db.create_view(
            name="chunks_view",
            view_type="arangosearch",
            properties=properties,
        )
    else:
        db.update_arangosearch_view(name="chunks_view", properties=properties)


def store_chunks(
    db,
    source_id: str,
    notebook_id: str,
    user_id: str,
    chunks: list[str],
    embeddings: list[list[float]],
    retrieval_version_id: str | None = None,
    embedding_model: str | None = None,
    embedding_dimensions: int | None = None,
) -> None:
    col = db.collection("chunks")
    docs = [
        {
            "_key": str(uuid.uuid4()).replace("-", ""),
            "source_id": source_id,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
            "embedding_model": embedding_model,
            "embedding_dimensions": embedding_dimensions,
            "chunk_index": i,
            "text": chunk,
            "embedding": embedding,
        }
        for i, (chunk, embedding) in enumerate(zip(chunks, embeddings))
    ]
    if docs:
        col.insert_many(docs)
        ensure_vector_index(db)


def delete_chunks(db, source_id: str, user_id: str, retrieval_version_id: str | None = None) -> None:
    if retrieval_version_id is not None:
        db.aql.execute(
            "FOR doc IN chunks FILTER doc.source_id == @sid AND doc.user_id == @uid AND doc.retrieval_version_id == @rv REMOVE doc IN chunks",
            bind_vars={"sid": source_id, "uid": user_id, "rv": retrieval_version_id},
        )
    else:
        # No version specified: scope the delete to chunks that themselves
        # have no retrieval_version_id, rather than wiping every version's
        # chunks for this source. A caller that wants to remove the entire
        # source (all versions) must use delete_document's explicit
        # full-source delete instead of relying on this implicit fallback.
        db.aql.execute(
            "FOR doc IN chunks FILTER doc.source_id == @sid AND doc.user_id == @uid "
            "AND doc.retrieval_version_id == null REMOVE doc IN chunks",
            bind_vars={"sid": source_id, "uid": user_id},
        )


def delete_all_source_chunks(db, source_id: str, user_id: str) -> None:
    """Explicitly delete every chunk for a source across all retrieval versions.

    Use this only when the entire source is being removed (e.g. document
    deletion). For reindex/version-scoped deletes, use delete_chunks with an
    explicit retrieval_version_id instead.
    """
    db.aql.execute(
        "FOR doc IN chunks FILTER doc.source_id == @sid AND doc.user_id == @uid REMOVE doc IN chunks",
        bind_vars={"sid": source_id, "uid": user_id},
    )


def delete_version_chunks(db, notebook_id: str, user_id: str, retrieval_version_id: str) -> None:
    db.aql.execute(
        "FOR doc IN chunks FILTER doc.notebook_id == @nid AND doc.user_id == @uid AND doc.retrieval_version_id == @rv REMOVE doc IN chunks",
        bind_vars={"nid": notebook_id, "uid": user_id, "rv": retrieval_version_id},
    )


def delete_notebook_payload(db, notebook_id: str, user_id: str) -> None:
    db.aql.execute(
        "FOR doc IN chunks FILTER doc.notebook_id == @nid AND doc.user_id == @uid REMOVE doc IN chunks",
        bind_vars={"nid": notebook_id, "uid": user_id},
    )
    db.aql.execute(
        "FOR doc IN documents FILTER doc.notebook_id == @nid AND doc.user_id == @uid REMOVE doc IN documents",
        bind_vars={"nid": notebook_id, "uid": user_id},
    )


def delete_user_payload(db, user_id: str) -> None:
    for collection in ("chunks", "documents", "experiments"):
        db.aql.execute(
            f"FOR doc IN {collection} FILTER doc.user_id == @uid REMOVE doc IN {collection}",
            bind_vars={"uid": user_id},
        )
    # Graph data (Phase 19) must be wiped alongside everything else on
    # account deletion -- otherwise entities/facts/edges outlive the user
    # who owns them (Gate B review fix).
    delete_all_user_graph_payload(db, user_id)


def search_vector(
    db,
    query_embedding: list[float],
    notebook_id: str,
    user_id: str,
    top_k: int = 5,
    retrieval_version_id: str | None = None,
) -> list[dict]:
    col = db.collection("chunks")
    existing = {idx["type"] for idx in col.indexes()}
    if "vector" not in existing:
        return []

    aql = """
    FOR doc IN chunks
      FILTER doc.notebook_id == @notebook_id
        AND doc.user_id == @user_id
        AND (@retrieval_version_id == null OR doc.retrieval_version_id == @retrieval_version_id)
      SORT APPROX_NEAR_COSINE(doc.embedding, @query_vec) DESC
      LIMIT @top_k
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        retrieval_version_id: doc.retrieval_version_id,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "notebook_id": notebook_id,
            "user_id": user_id,
            "query_vec": query_embedding,
            "top_k": top_k,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return list(cursor)


def search_bm25(
    db,
    query: str,
    notebook_id: str,
    user_id: str,
    top_k: int = 5,
    retrieval_version_id: str | None = None,
) -> list[dict]:
    aql = """
    FOR doc IN chunks_view
      SEARCH doc.notebook_id == @notebook_id
        AND doc.user_id == @user_id
        AND (@retrieval_version_id == null OR doc.retrieval_version_id == @retrieval_version_id)
        AND ANALYZER(doc.text IN TOKENS(@query, 'text_en'), 'text_en')
      SORT BM25(doc) DESC
      LIMIT @top_k
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        retrieval_version_id: doc.retrieval_version_id,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "notebook_id": notebook_id,
            "user_id": user_id,
            "query": query,
            "top_k": top_k,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return list(cursor)


def search_hybrid(
    db,
    query_embedding: list[float],
    query: str,
    notebook_id: str,
    user_id: str,
    top_k: int = 5,
    alpha: float = 0.5,
    retrieval_version_id: str | None = None,
) -> list[dict]:
    fetch_k = min(top_k * 3, 60)
    vec_results = search_vector(db, query_embedding, notebook_id, user_id, fetch_k, retrieval_version_id)
    bm25_results = search_bm25(db, query, notebook_id, user_id, fetch_k, retrieval_version_id)

    k_rrf = 60
    scores: dict[tuple, float] = {}
    for rank, doc in enumerate(vec_results, start=1):
        key = (doc["source_id"], doc["chunk_index"])
        scores[key] = scores.get(key, 0.0) + alpha / (k_rrf + rank)
    for rank, doc in enumerate(bm25_results, start=1):
        key = (doc["source_id"], doc["chunk_index"])
        scores[key] = scores.get(key, 0.0) + (1.0 - alpha) / (k_rrf + rank)

    chunk_map: dict[tuple, dict] = {}
    for d in bm25_results:
        chunk_map[(d["source_id"], d["chunk_index"])] = d
    for d in vec_results:
        chunk_map[(d["source_id"], d["chunk_index"])] = d

    sorted_keys = sorted(scores, key=lambda k: scores[k], reverse=True)
    return [chunk_map[k] for k in sorted_keys[:top_k]]


def _resolve_chunk_doc_ids(
    db,
    notebook_id: str,
    user_id: str,
    retrieval_version_id: str | None,
    chunk_keys: list[tuple[str, int]],
) -> dict[tuple[str, int], str]:
    """Map (source_id, chunk_index) pairs to chunk _ids, scoped like every
    other graph query. Chunk _keys are random UUIDs (see ensure_collections),
    so callers that only have source_id/chunk_index must resolve through
    this lookup before traversing graph edges."""
    if not chunk_keys:
        return {}
    pairs = [[source_id, chunk_index] for source_id, chunk_index in chunk_keys]
    aql = """
    FOR doc IN chunks
      FILTER doc.notebook_id == @notebook_id
        AND doc.user_id == @user_id
        AND (@retrieval_version_id == null OR doc.retrieval_version_id == @retrieval_version_id)
        AND [doc.source_id, doc.chunk_index] IN @pairs
      RETURN { source_id: doc.source_id, chunk_index: doc.chunk_index, _id: doc._id }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
            "pairs": pairs,
        },
    )
    return {(row["source_id"], row["chunk_index"]): row["_id"] for row in cursor}


def _entity_ids_mentioned_by_chunks(
    db, chunk_doc_ids: list[str], notebook_id: str, user_id: str, retrieval_version_id: str | None
) -> list[str]:
    if not chunk_doc_ids:
        return []
    aql = """
    FOR edge IN chunk_mentions_entity
      FILTER edge._from IN @chunk_ids
        AND edge.notebook_id == @notebook_id
        AND edge.user_id == @user_id
        AND (@retrieval_version_id == null OR edge.retrieval_version_id == @retrieval_version_id)
      RETURN DISTINCT edge._to
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "chunk_ids": chunk_doc_ids,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return list(cursor)


def _fact_ids_for_entities(
    db, entity_doc_ids: list[str], notebook_id: str, user_id: str, retrieval_version_id: str | None
) -> list[str]:
    if not entity_doc_ids:
        return []
    aql = """
    FOR edge IN fact_has_participant
      FILTER edge._to IN @entity_ids
        AND edge.notebook_id == @notebook_id
        AND edge.user_id == @user_id
        AND (@retrieval_version_id == null OR edge.retrieval_version_id == @retrieval_version_id)
      RETURN DISTINCT edge._from
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "entity_ids": entity_doc_ids,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return list(cursor)


def _entity_ids_participating_in_facts(
    db, fact_doc_ids: list[str], notebook_id: str, user_id: str, retrieval_version_id: str | None
) -> list[str]:
    if not fact_doc_ids:
        return []
    aql = """
    FOR edge IN fact_has_participant
      FILTER edge._from IN @fact_ids
        AND edge.notebook_id == @notebook_id
        AND edge.user_id == @user_id
        AND (@retrieval_version_id == null OR edge.retrieval_version_id == @retrieval_version_id)
      RETURN DISTINCT edge._to
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "fact_ids": fact_doc_ids,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return list(cursor)


def _expand_fact_ids(
    db,
    seed_entity_ids: list[str],
    notebook_id: str,
    user_id: str,
    retrieval_version_id: str | None,
    max_graph_hops: int,
) -> list[str]:
    """Walk entity -> fact -> (shared entities) -> fact ... up to
    max_graph_hops fact-hops, returning every fact id reached, in the order
    first discovered (closer hops first)."""
    seen_facts: list[str] = []
    seen_fact_set: set[str] = set()
    entity_ids = seed_entity_ids
    for _ in range(max(1, max_graph_hops)):
        new_fact_ids = [
            fid for fid in _fact_ids_for_entities(db, entity_ids, notebook_id, user_id, retrieval_version_id)
            if fid not in seen_fact_set
        ]
        if not new_fact_ids:
            break
        seen_fact_set.update(new_fact_ids)
        seen_facts.extend(new_fact_ids)
        entity_ids = _entity_ids_participating_in_facts(db, new_fact_ids, notebook_id, user_id, retrieval_version_id)
    return seen_facts


def search_graph_branch(
    db,
    notebook_id: str,
    user_id: str,
    seed_chunks: list[dict],
    retrieval_version_id: str | None = None,
    max_graph_hops: int = 1,
    max_fact_hits: int = 8,
) -> list[dict]:
    """Seed from a set of chunk hits (as returned by search_vector/search_bm25),
    traverse chunk_mentions_entity -> fact_has_participant -> fact_supported_by_chunk
    to find related facts, and return them as chunk-shaped candidates carrying
    fact provenance so they can be fused into the existing RRF alongside
    vector + BM25 results.

    Never raises: on a non-graph-enabled version (no entities/facts written),
    every intermediate lookup simply returns empty lists and this function
    returns []."""
    if max_fact_hits < 1 or not seed_chunks:
        return []

    chunk_keys = [(c["source_id"], c["chunk_index"]) for c in seed_chunks]
    chunk_id_by_key = _resolve_chunk_doc_ids(db, notebook_id, user_id, retrieval_version_id, chunk_keys)
    seed_chunk_ids = list(chunk_id_by_key.values())
    if not seed_chunk_ids:
        return []

    seed_entity_ids = _entity_ids_mentioned_by_chunks(db, seed_chunk_ids, notebook_id, user_id, retrieval_version_id)
    if not seed_entity_ids:
        return []

    fact_ids = _expand_fact_ids(db, seed_entity_ids, notebook_id, user_id, retrieval_version_id, max_graph_hops)
    if not fact_ids:
        return []
    fact_ids = fact_ids[:max_fact_hits]

    aql = """
    FOR fact IN facts
      FILTER fact._key IN @fact_keys
      LET participant_names = (
        FOR edge IN fact_has_participant
          FILTER edge._from == fact._id
          FOR entity IN entities
            FILTER entity._id == edge._to
            RETURN entity.canonical_name
      )
      LET supporting_chunks = (
        FOR edge IN fact_supported_by_chunk
          FILTER edge._from == fact._id
          FOR chunk IN chunks
            FILTER chunk._id == edge._to
            RETURN { source_id: chunk.source_id, chunk_index: chunk.chunk_index, text: chunk.text,
                     retrieval_version_id: chunk.retrieval_version_id }
      )
      RETURN { fact_id: fact._key, fact_text: fact.statement_text, confidence: fact.confidence,
               participants: participant_names, supporting_chunks: supporting_chunks }
    """
    fact_keys = [fid.split("/", 1)[1] if "/" in fid else fid for fid in fact_ids]
    cursor = db.aql.execute(
        aql,
        bind_vars={"fact_keys": fact_keys},
    )
    fact_rows = list(cursor)
    fact_rows.sort(key=lambda row: row.get("confidence") or 0.0, reverse=True)

    results: list[dict] = []
    for row in fact_rows[:max_fact_hits]:
        for chunk in row["supporting_chunks"]:
            results.append({
                "source_id": chunk["source_id"],
                "chunk_index": chunk["chunk_index"],
                "retrieval_version_id": chunk["retrieval_version_id"],
                "text": chunk["text"],
                "fact_id": row["fact_id"],
                "fact_text": row["fact_text"],
                "participants": row["participants"],
            })
    return results


def search_graph_hybrid(
    db,
    query_embedding: list[float],
    query: str,
    notebook_id: str,
    user_id: str,
    top_k: int = 5,
    alpha: float = 0.5,
    retrieval_version_id: str | None = None,
    max_graph_hops: int = 1,
    max_fact_hits: int = 8,
) -> list[dict]:
    """Like search_hybrid, but also seeds a graph branch from the top vector
    hits and fuses it into the same Python-side RRF rather than adding a
    separate AQL join (matches the existing fusion design)."""
    fetch_k = min(top_k * 3, 60)
    vec_results = search_vector(db, query_embedding, notebook_id, user_id, fetch_k, retrieval_version_id)
    bm25_results = search_bm25(db, query, notebook_id, user_id, fetch_k, retrieval_version_id)
    graph_results = search_graph_branch(
        db, notebook_id, user_id, vec_results[:top_k], retrieval_version_id, max_graph_hops, max_fact_hits
    )

    k_rrf = 60
    scores: dict[tuple, float] = {}
    chunk_map: dict[tuple, dict] = {}
    provenance: dict[tuple, dict] = {}

    def add_ranked(results: list[dict], weight: float) -> None:
        for rank, doc in enumerate(results, start=1):
            key = (doc["source_id"], doc["chunk_index"])
            scores[key] = scores.get(key, 0.0) + weight / (k_rrf + rank)
            chunk_map[key] = doc

    add_ranked(vec_results, alpha)
    add_ranked(bm25_results, 1.0 - alpha)
    add_ranked(graph_results, 1.0)
    for doc in graph_results:
        key = (doc["source_id"], doc["chunk_index"])
        provenance[key] = {
            "fact_id": doc["fact_id"],
            "fact_text": doc["fact_text"],
            "participants": doc["participants"],
        }

    sorted_keys = sorted(scores, key=lambda k: scores[k], reverse=True)
    results: list[dict] = []
    for key in sorted_keys[:top_k]:
        item = dict(chunk_map[key])
        if key in provenance:
            item.update(provenance[key])
        results.append(item)
    return results


def get_chunk_ids_by_index(
    db,
    source_id: str,
    notebook_id: str,
    user_id: str,
    retrieval_version_id: str | None = None,
) -> dict[int, str]:
    """Map chunk_index -> Arango _id for a source, scoped like every other query.

    Used by the graph assembler (Gate B) to attach mention/fact edges to the
    chunk a piece of extracted text actually came from.
    """
    aql = """
    FOR doc IN chunks
      FILTER doc.source_id == @source_id
        AND doc.notebook_id == @notebook_id
        AND doc.user_id == @user_id
        AND (@retrieval_version_id == null OR doc.retrieval_version_id == @retrieval_version_id)
      RETURN { chunk_index: doc.chunk_index, _id: doc._id }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "source_id": source_id,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    return {row["chunk_index"]: row["_id"] for row in cursor}


_GRAPH_PAYLOAD_COLLECTIONS = (
    "entities",
    "facts",
    "chunk_mentions_entity",
    "fact_has_participant",
    "fact_supported_by_chunk",
)


def delete_graph_payload(db, notebook_id: str, user_id: str, retrieval_version_id: str | None) -> None:
    """Delete entities/facts/edges for a notebook, mirroring delete_version_chunks
    so graph data is retired exactly when its chunks are.

    retrieval_version_id=None means "every version" -- use this for full
    notebook wipes; pass an explicit version id to scope the delete to just
    that version (e.g. the chunk-prune path)."""
    for collection in _GRAPH_PAYLOAD_COLLECTIONS:
        db.aql.execute(
            f"FOR doc IN {collection} FILTER doc.notebook_id == @nid AND doc.user_id == @uid "
            f"AND (@rv == null OR doc.retrieval_version_id == @rv) REMOVE doc IN {collection}",
            bind_vars={"nid": notebook_id, "uid": user_id, "rv": retrieval_version_id},
        )


def delete_source_graph_payload(db, source_id: str, user_id: str) -> None:
    """Delete entity-mention/fact-support edges tied to a single source's
    chunks, across all retrieval versions, mirroring delete_all_source_chunks.

    Must be called BEFORE the source's chunks are removed (e.g. before
    delete_all_source_chunks) since it looks the chunks up by source_id to
    find the chunk _ids referenced by graph edges. Entities/facts themselves
    aren't keyed by source_id (they can be supported by chunks from multiple
    sources within a version), so this only prunes the edges anchored to this
    source's chunks; it leaves entities/facts in place even if this was their
    only supporting edge, consistent with the version-scoped delete_graph_payload
    which also only removes edges/vertices it owns directly.
    """
    db.aql.execute(
        "LET chunk_ids = (FOR c IN chunks FILTER c.source_id == @sid AND c.user_id == @uid RETURN c._id) "
        "FOR edge IN chunk_mentions_entity FILTER edge._from IN chunk_ids REMOVE edge IN chunk_mentions_entity",
        bind_vars={"sid": source_id, "uid": user_id},
    )
    db.aql.execute(
        "LET chunk_ids = (FOR c IN chunks FILTER c.source_id == @sid AND c.user_id == @uid RETURN c._id) "
        "FOR edge IN fact_supported_by_chunk FILTER edge._to IN chunk_ids REMOVE edge IN fact_supported_by_chunk",
        bind_vars={"sid": source_id, "uid": user_id},
    )


def delete_all_notebook_graph_payload(db, notebook_id: str, user_id: str) -> None:
    """Delete entities/facts/edges for every retrieval version of a notebook.

    Use this for full notebook wipes (delete_notebook_payload's graph
    counterpart); for a single version use delete_graph_payload instead.
    """
    delete_graph_payload(db, notebook_id, user_id, None)


def delete_all_user_graph_payload(db, user_id: str) -> None:
    """Delete every entity/fact/edge owned by a user, across all notebooks
    and retrieval versions. Use this for account deletion (delete_user_payload's
    graph counterpart)."""
    for collection in _GRAPH_PAYLOAD_COLLECTIONS:
        db.aql.execute(
            f"FOR doc IN {collection} FILTER doc.user_id == @uid REMOVE doc IN {collection}",
            bind_vars={"uid": user_id},
        )


def get_source_content(
    db,
    source_id: str,
    notebook_id: str,
    user_id: str,
    max_chars: int = 12000,
    retrieval_version_id: str | None = None,
) -> dict:
    aql = """
    FOR doc IN chunks
      FILTER doc.source_id == @source_id
        AND doc.notebook_id == @notebook_id
        AND doc.user_id == @user_id
        AND (@retrieval_version_id == null OR doc.retrieval_version_id == @retrieval_version_id)
      SORT doc.chunk_index ASC
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "source_id": source_id,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        },
    )
    chunks = list(cursor)
    returned_chunks: list[dict] = []
    text_parts: list[str] = []
    remaining = max_chars
    truncated = False
    for chunk in chunks:
        if remaining <= 0:
            truncated = True
            break
        original_text = chunk["text"]
        part = original_text[:remaining]
        if len(part) < len(original_text):
            truncated = True
        text_parts.append(f"[chunk {chunk['chunk_index']}]\n{part}")
        returned_chunks.append({**chunk, "text": part})
        remaining -= len(part)
    return {
        "source_id": source_id,
        "notebook_id": notebook_id,
        "chunks": returned_chunks,
        "text": "\n\n---\n\n".join(text_parts),
        "truncated": truncated,
    }
