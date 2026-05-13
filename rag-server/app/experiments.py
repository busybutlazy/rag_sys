import time
import uuid
from datetime import UTC, datetime

from app import embedder, vector_store
from app.models import ExperimentRecord, ExperimentRunRequest

_VALID_MODES = {"vector", "bm25", "hybrid"}


async def run_experiment(db, req: ExperimentRunRequest) -> dict:
    modes = [m for m in req.config.modes if m in _VALID_MODES]
    if not modes:
        raise ValueError("At least one valid mode is required")

    top_k = max(1, min(20, req.config.top_k))
    alpha = max(0.0, min(1.0, req.config.alpha))
    queries = [q.strip() for q in req.queries if q.strip()]
    if not queries:
        raise ValueError("At least one query is required")
    if len(queries) > 20:
        raise ValueError("At most 20 queries are allowed")

    results = []
    for query in queries:
        query_embedding = None
        if "vector" in modes or "hybrid" in modes:
            query_embedding = await embedder.embed(query)

        for mode in modes:
            start = time.perf_counter()
            if mode == "vector":
                docs = vector_store.search_vector(db, query_embedding, req.notebook_id, top_k)
            elif mode == "bm25":
                docs = vector_store.search_bm25(db, query, req.notebook_id, top_k)
            else:
                docs = vector_store.search_hybrid(db, query_embedding, query, req.notebook_id, top_k, alpha)
            latency_ms = int((time.perf_counter() - start) * 1000)
            results.append({
                "query": query,
                "mode": mode,
                "latency_ms": latency_ms,
                "result_count": len(docs),
                "results": [
                    {"source_id": d["source_id"], "chunk_index": d["chunk_index"]}
                    for d in docs
                ],
            })

    now = datetime.now(UTC).isoformat()
    record = {
        "_key": uuid.uuid4().hex,
        "notebook_id": req.notebook_id,
        "name": req.name or f"Experiment {now}",
        "config": {"modes": modes, "top_k": top_k, "alpha": alpha},
        "queries": queries,
        "results": results,
        "created_at": now,
    }
    db.collection("experiments").insert(record)
    return _public(record)


def list_experiments(db, notebook_id: str, limit: int = 20) -> list[dict]:
    cursor = db.aql.execute(
        """
        FOR doc IN experiments
          FILTER doc.notebook_id == @notebook_id
          SORT doc.created_at DESC
          LIMIT @limit
          RETURN doc
        """,
        bind_vars={"notebook_id": notebook_id, "limit": limit},
    )
    return [_public(doc) for doc in cursor]


def get_experiment(db, experiment_id: str, notebook_id: str) -> dict | None:
    doc = db.collection("experiments").get(experiment_id)
    if not doc or doc.get("notebook_id") != notebook_id:
        return None
    return _public(doc)


def _public(doc: dict) -> dict:
    return {
        "id": doc["_key"],
        "notebook_id": doc["notebook_id"],
        "name": doc["name"],
        "config": doc["config"],
        "queries": doc["queries"],
        "results": doc["results"],
        "created_at": doc["created_at"],
    }


def validate_record(record: dict) -> ExperimentRecord:
    return ExperimentRecord(**record)
