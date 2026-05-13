import uuid
import time
from arango.exceptions import IndexCreateError
from app.embedder import DIMENSIONS


def ensure_collections(db) -> None:
    for name in ["documents", "chunks", "notebooks"]:
        if not db.has_collection(name):
            db.create_collection(name)


def ensure_vector_index(db) -> None:
    col = db.collection("chunks")
    existing = {idx["type"] for idx in col.indexes()}
    if "vector" not in existing:
        for attempt in range(30):
            try:
                col.add_index({
                    "type": "vector",
                    "fields": ["embedding"],
                    "params": {
                        "metric": "cosine",
                        "dimension": DIMENSIONS,
                        "nLists": 2,
                    },
                })
                return
            except IndexCreateError as exc:
                if "vector index not ready" not in str(exc) or attempt == 29:
                    raise
                time.sleep(5)


def ensure_search_view(db) -> None:
    existing = {v["name"] for v in db.views()}
    if "chunks_view" not in existing:
        db.create_view(
            name="chunks_view",
            view_type="arangosearch",
            properties={
                "links": {
                    "chunks": {
                        "fields": {
                            "text": {"analyzers": ["text_en"]},
                            "notebook_id": {"analyzers": ["identity"]},
                            "source_id": {"analyzers": ["identity"]},
                            "chunk_index": {},
                        }
                    }
                }
            },
        )


def store_chunks(
    db,
    source_id: str,
    notebook_id: str,
    chunks: list[str],
    embeddings: list[list[float]],
) -> None:
    col = db.collection("chunks")
    docs = [
        {
            "_key": str(uuid.uuid4()).replace("-", ""),
            "source_id": source_id,
            "notebook_id": notebook_id,
            "chunk_index": i,
            "text": chunk,
            "embedding": embedding,
        }
        for i, (chunk, embedding) in enumerate(zip(chunks, embeddings))
    ]
    if docs:
        col.insert_many(docs)


def delete_chunks(db, source_id: str) -> None:
    db.aql.execute(
        "FOR doc IN chunks FILTER doc.source_id == @sid REMOVE doc IN chunks",
        bind_vars={"sid": source_id},
    )


def search_vector(
    db,
    query_embedding: list[float],
    notebook_id: str,
    top_k: int = 5,
) -> list[dict]:
    aql = """
    FOR doc IN chunks
      FILTER doc.notebook_id == @notebook_id
      SORT APPROX_NEAR_COSINE(doc.embedding, @query_vec)
      LIMIT @top_k
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={
            "notebook_id": notebook_id,
            "query_vec": query_embedding,
            "top_k": top_k,
        },
    )
    return list(cursor)


def search_bm25(
    db,
    query: str,
    notebook_id: str,
    top_k: int = 5,
) -> list[dict]:
    aql = """
    FOR doc IN chunks_view
      SEARCH doc.notebook_id == @notebook_id
        AND ANALYZER(doc.text IN TOKENS(@query, 'text_en'), 'text_en')
      SORT BM25(doc) DESC
      LIMIT @top_k
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={"notebook_id": notebook_id, "query": query, "top_k": top_k},
    )
    return list(cursor)


def search_hybrid(
    db,
    query_embedding: list[float],
    query: str,
    notebook_id: str,
    top_k: int = 5,
    alpha: float = 0.5,
) -> list[dict]:
    fetch_k = min(top_k * 3, 60)
    vec_results = search_vector(db, query_embedding, notebook_id, fetch_k)
    bm25_results = search_bm25(db, query, notebook_id, fetch_k)

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


def get_source_content(
    db,
    source_id: str,
    notebook_id: str,
    max_chars: int = 12000,
) -> dict:
    aql = """
    FOR doc IN chunks
      FILTER doc.source_id == @source_id
        AND doc.notebook_id == @notebook_id
      SORT doc.chunk_index ASC
      RETURN {
        source_id: doc.source_id,
        chunk_index: doc.chunk_index,
        text: doc.text
      }
    """
    cursor = db.aql.execute(
        aql,
        bind_vars={"source_id": source_id, "notebook_id": notebook_id},
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
