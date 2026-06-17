import hashlib
import re
import unicodedata

from app.vector_store import get_chunk_ids_by_index

# Phase 19 Gate B: deterministic resolve + assemble for the entity/fact
# graph layer. No LLM usage here -- extraction happens in ai-server; this
# module only normalizes names and writes the already-extracted structure.
# See docs/superpowers/plans/phase-19-graphrag-foundations.md.


def normalize_entity_name(name: str) -> str:
    """Literal-rule alias resolution: NFKC -> casefold -> strip punctuation
    -> collapse whitespace. Intentionally not fuzzy -- see Gate B open
    questions in the phase-19 plan for why."""
    normalized = unicodedata.normalize("NFKC", name)
    normalized = normalized.casefold()
    normalized = re.sub(r"[^\w\s]", "", normalized)
    normalized = re.sub(r"\s+", " ", normalized).strip()
    return normalized


def _deterministic_key(*parts: str) -> str:
    raw = "\x1f".join(parts)
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()[:32]


def entity_key(notebook_id: str, retrieval_version_id: str | None, canonical_name: str) -> str:
    return _deterministic_key("entity", notebook_id, retrieval_version_id or "", canonical_name)


def fact_key(
    notebook_id: str,
    retrieval_version_id: str | None,
    chunk_index: int,
    position: int,
    predicate: str,
) -> str:
    # Keyed by originating chunk + position so re-ingesting the same
    # retrieval version (e.g. a retried job) upserts rather than duplicates.
    return _deterministic_key(
        "fact", notebook_id, retrieval_version_id or "", str(chunk_index), str(position), predicate
    )


def resolve_and_assemble(
    db,
    source_id: str,
    notebook_id: str,
    user_id: str,
    retrieval_version_id: str | None,
    chunk_extractions: list[dict],
) -> dict:
    chunk_ids = get_chunk_ids_by_index(db, source_id, user_id, retrieval_version_id)

    entities: dict[str, dict] = {}
    facts: dict[str, dict] = {}
    chunk_mention_edges: dict[str, dict] = {}
    fact_participant_edges: dict[str, dict] = {}
    fact_chunk_edges: dict[str, dict] = {}
    skipped_chunks: list[int] = []

    def upsert_entity(name: str, entity_type: str) -> str | None:
        canonical = normalize_entity_name(name)
        if not canonical:
            return None
        key = entity_key(notebook_id, retrieval_version_id, canonical)
        if key not in entities:
            entities[key] = {
                "_key": key,
                "notebook_id": notebook_id,
                "user_id": user_id,
                "retrieval_version_id": retrieval_version_id,
                "canonical_name": canonical,
                "entity_type": entity_type,
                "aliases": [],
                "mention_count": 0,
            }
        doc = entities[key]
        doc["mention_count"] += 1
        if name != canonical and name not in doc["aliases"]:
            doc["aliases"].append(name)
        return key

    for chunk in chunk_extractions:
        chunk_index = chunk["chunk_index"]
        chunk_id = chunk_ids.get(chunk_index)
        if chunk_id is None:
            # The chunk this extraction refers to doesn't exist (wrong
            # version, already deleted, bad input) -- skip rather than
            # write an edge to nothing.
            skipped_chunks.append(chunk_index)
            continue

        for mention in chunk.get("mentions", []):
            entity_k = upsert_entity(mention["entity_name"], mention.get("entity_type") or "unknown")
            if entity_k is None:
                continue
            edge_key = _deterministic_key("chunk_mentions_entity", chunk_id, entity_k)
            chunk_mention_edges[edge_key] = {
                "_key": edge_key,
                "_from": chunk_id,
                "_to": f"entities/{entity_k}",
                "notebook_id": notebook_id,
                "user_id": user_id,
                "retrieval_version_id": retrieval_version_id,
            }

        for position, fact in enumerate(chunk.get("facts", [])):
            predicate = fact["predicate"]
            fkey = fact_key(notebook_id, retrieval_version_id, chunk_index, position, predicate)
            facts[fkey] = {
                "_key": fkey,
                "notebook_id": notebook_id,
                "user_id": user_id,
                "retrieval_version_id": retrieval_version_id,
                "predicate": predicate,
                "statement_text": fact.get("statement_text") or "",
                "confidence": fact.get("confidence") or 0.0,
            }
            fact_chunk_edge_key = _deterministic_key("fact_supported_by_chunk", fkey, chunk_id)
            fact_chunk_edges[fact_chunk_edge_key] = {
                "_key": fact_chunk_edge_key,
                "_from": f"facts/{fkey}",
                "_to": chunk_id,
                "notebook_id": notebook_id,
                "user_id": user_id,
                "retrieval_version_id": retrieval_version_id,
            }
            for participant in fact.get("participants", []):
                entity_k = upsert_entity(participant["entity_name"], "unknown")
                if entity_k is None:
                    continue
                role = participant.get("role") or ""
                participant_edge_key = _deterministic_key("fact_has_participant", fkey, entity_k, role)
                fact_participant_edges[participant_edge_key] = {
                    "_key": participant_edge_key,
                    "_from": f"facts/{fkey}",
                    "_to": f"entities/{entity_k}",
                    "role": role,
                    "notebook_id": notebook_id,
                    "user_id": user_id,
                    "retrieval_version_id": retrieval_version_id,
                }

    if entities:
        db.collection("entities").insert_many(list(entities.values()), overwrite_mode="replace")
    if facts:
        db.collection("facts").insert_many(list(facts.values()), overwrite_mode="replace")
    if chunk_mention_edges:
        db.collection("chunk_mentions_entity").insert_many(
            list(chunk_mention_edges.values()), overwrite_mode="replace"
        )
    if fact_participant_edges:
        db.collection("fact_has_participant").insert_many(
            list(fact_participant_edges.values()), overwrite_mode="replace"
        )
    if fact_chunk_edges:
        db.collection("fact_supported_by_chunk").insert_many(
            list(fact_chunk_edges.values()), overwrite_mode="replace"
        )

    return {
        "entities_written": len(entities),
        "facts_written": len(facts),
        "edges_written": len(chunk_mention_edges) + len(fact_participant_edges) + len(fact_chunk_edges),
        "skipped_chunks": skipped_chunks,
    }
