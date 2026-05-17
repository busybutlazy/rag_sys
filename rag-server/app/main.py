import os
import asyncio
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException, Header, Query
from app.db import get_db
from app.json_logging import configure_json_logging
from app.models import (
    BenchmarkResponse,
    ExperimentRecord,
    ExperimentRunRequest,
    IngestRequest,
    SearchResponse,
    SourceContentResponse,
)
from app.rag_config import current_config, validate_config
from app import chunker, embedder, experiments, vector_store
from app.metrics import metrics, observe_http

configure_json_logging()

_MIN_SECRET_LEN = 32
_INTERNAL_SECRET = os.environ.get("RAG_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
if len(_INTERNAL_SECRET) < _MIN_SECRET_LEN:
    raise SystemExit(f"RAG_INTERNAL_SECRET must be at least {_MIN_SECRET_LEN} characters")

_cfg = current_config()
validate_config(_cfg)


def _check_secret(x_internal_secret: str | None) -> None:
    if x_internal_secret != _INTERNAL_SECRET:
        raise HTTPException(status_code=403, detail="Forbidden")


@asynccontextmanager
async def lifespan(_: FastAPI):
    db = get_db()
    vector_store.ensure_collections(db)
    vector_store.ensure_vector_index(db)
    vector_store.ensure_search_view(db)
    yield


app = FastAPI(title="RAG Server", version="0.1.0", lifespan=lifespan)
app.middleware("http")(observe_http)


@app.get("/health")
async def health():
    return {"status": "ok", "service": "rag-server"}


@app.get("/ready")
async def ready():
    get_db().version()
    return {"status": "ready", "service": "rag-server"}


@app.get("/metrics")
async def get_metrics():
    return metrics.snapshot()


@app.post("/ingest", status_code=200)
async def ingest(
    req: IngestRequest,
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    metrics.increment("ingest_requests")

    if not os.path.exists(req.file_path):
        raise HTTPException(status_code=404, detail=f"File not found: {req.file_path}")

    db = get_db()
    docs_col = db.collection("documents")

    retrieval = req.retrieval
    ingestion_config = {
        "chunk_size": retrieval.chunk_size if retrieval else _cfg.chunk_size,
        "chunk_overlap": retrieval.chunk_overlap if retrieval else _cfg.chunk_overlap,
        "embedding_model": retrieval.embedding_model if retrieval else _cfg.embedding_model,
        "embedding_dimensions": retrieval.embedding_dimensions if retrieval else _cfg.embedding_dimensions,
    }

    doc_record = {
        "_key": req.source_id,
        "source_id": req.source_id,
        "notebook_id": req.notebook_id,
        "user_id": req.user_id,
        "retrieval_version_id": retrieval.retrieval_version_id if retrieval else None,
        "file_path": req.file_path,
        "mime_type": req.mime_type,
        "status": "processing",
        "ingestion_config": ingestion_config,
    }
    if docs_col.has(req.source_id):
        docs_col.update(doc_record)
    else:
        docs_col.insert(doc_record)

    try:
        text = await asyncio.wait_for(
            asyncio.to_thread(chunker.extract_text, req.file_path, req.mime_type),
            timeout=float(os.environ.get("PARSER_TIMEOUT_SECONDS", "30")),
        )
        chunks = chunker.chunk_text(text, ingestion_config["chunk_size"], ingestion_config["chunk_overlap"])
        if not chunks:
            raise ValueError("No text extracted from file")

        embeddings = await embedder.embed_batch(chunks)

        # When a retrieval version is specified, only remove chunks for that
        # specific version so other versions' chunks are preserved across reindexes.
        target_version_id = retrieval.retrieval_version_id if retrieval else None
        vector_store.delete_chunks(db, req.source_id, req.user_id, target_version_id)
        vector_store.store_chunks(
            db,
            req.source_id,
            req.notebook_id,
            req.user_id,
            chunks,
            embeddings,
            target_version_id,
            ingestion_config["embedding_model"],
            ingestion_config["embedding_dimensions"],
        )

        docs_col.update({
            "_key": req.source_id,
            "status": "ready",
            "chunk_count": len(chunks),
            "ingestion_config": {**ingestion_config, "chunk_count": len(chunks)},
        })
        return {"source_id": req.source_id, "status": "ready", "chunk_count": len(chunks)}

    except Exception as exc:
        docs_col.update({"_key": req.source_id, "status": "error", "error": str(exc)})
        raise HTTPException(status_code=500, detail=str(exc))


@app.get("/search/vector", response_model=SearchResponse)
async def search_vector(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    user_id: str = Query(..., description="Owner scope for the notebook"),
    top_k: int = Query(default=None, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    metrics.increment("search_vector")
    effective_top_k = top_k if top_k is not None else _cfg.top_k
    query_embedding = await embedder.embed(q)
    results = vector_store.search_vector(get_db(), query_embedding, notebook_id, user_id, effective_top_k)
    return SearchResponse(results=results)


@app.get("/search/bm25", response_model=SearchResponse)
async def search_bm25_endpoint(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    user_id: str = Query(..., description="Owner scope for the notebook"),
    top_k: int = Query(default=None, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    metrics.increment("search_bm25")
    effective_top_k = top_k if top_k is not None else _cfg.top_k
    results = vector_store.search_bm25(get_db(), q, notebook_id, user_id, effective_top_k)
    return SearchResponse(results=results)


@app.get("/search/hybrid", response_model=SearchResponse)
async def search_hybrid_endpoint(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    user_id: str = Query(..., description="Owner scope for the notebook"),
    top_k: int = Query(default=None, ge=1, le=20),
    alpha: float = Query(default=None, ge=0.0, le=1.0, description="Vector weight (1-alpha goes to BM25)"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    metrics.increment("search_hybrid")
    effective_top_k = top_k if top_k is not None else _cfg.top_k
    effective_alpha = alpha if alpha is not None else _cfg.hybrid_alpha
    query_embedding = await embedder.embed(q)
    results = vector_store.search_hybrid(get_db(), query_embedding, q, notebook_id, user_id, effective_top_k, effective_alpha)
    return SearchResponse(results=results)


@app.get("/search/benchmark", response_model=BenchmarkResponse)
async def search_benchmark(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    user_id: str = Query(..., description="Owner scope for the notebook"),
    top_k: int = Query(default=None, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    effective_top_k = top_k if top_k is not None else _cfg.top_k
    query_embedding = await embedder.embed(q)
    db = get_db()
    vec_results = vector_store.search_vector(db, query_embedding, notebook_id, user_id, effective_top_k)
    bm25_results = vector_store.search_bm25(db, q, notebook_id, user_id, effective_top_k)
    hybrid_results = vector_store.search_hybrid(db, query_embedding, q, notebook_id, user_id, effective_top_k)
    return BenchmarkResponse(
        query=q,
        vector=vec_results,
        bm25=bm25_results,
        hybrid=hybrid_results,
    )


@app.delete("/documents/{source_id}", status_code=204)
async def delete_document(
    source_id: str,
    user_id: str = Query(..., description="Owner scope for the source"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    db = get_db()
    vector_store.delete_chunks(db, source_id, user_id)
    docs_col = db.collection("documents")
    doc = docs_col.get(source_id)
    if doc and doc.get("user_id") == user_id:
        docs_col.delete(source_id)


@app.delete("/documents/{source_id}/chunks", status_code=204)
async def delete_source_version_chunks(
    source_id: str,
    user_id: str = Query(..., description="Owner scope for the source"),
    retrieval_version_id: str | None = Query(default=None, description="If set, only delete chunks for this version"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    vector_store.delete_chunks(get_db(), source_id, user_id, retrieval_version_id)


@app.delete("/notebooks/{notebook_id}/documents", status_code=204)
async def delete_notebook_documents(
    notebook_id: str,
    user_id: str = Query(..., description="Owner scope for the notebook"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    vector_store.delete_notebook_payload(get_db(), notebook_id, user_id)


@app.delete("/notebooks/{notebook_id}/chunks", status_code=204)
async def delete_notebook_version_chunks(
    notebook_id: str,
    user_id: str = Query(..., description="Owner scope for the notebook"),
    retrieval_version_id: str = Query(..., description="Delete chunks for this retrieval version only"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    vector_store.delete_version_chunks(get_db(), notebook_id, user_id, retrieval_version_id)


@app.get("/documents/{source_id}/content", response_model=SourceContentResponse)
async def get_document_content(
    source_id: str,
    notebook_id: str = Query(..., description="Notebook scope for the source"),
    user_id: str = Query(..., description="Owner scope for the source"),
    max_chars: int = Query(default=12000, ge=1000, le=50000),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    content = vector_store.get_source_content(get_db(), source_id, notebook_id, user_id, max_chars)
    if not content["chunks"]:
        raise HTTPException(status_code=404, detail="Source content not found")
    return SourceContentResponse(**content)


@app.post("/experiments/run", response_model=ExperimentRecord)
async def run_experiment(
    req: ExperimentRunRequest,
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    try:
        record = await experiments.run_experiment(get_db(), req)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc))
    return experiments.validate_record(record)


@app.get("/experiments", response_model=list[ExperimentRecord])
async def list_experiments(
    notebook_id: str = Query(...),
    user_id: str = Query(...),
    limit: int = Query(default=20, ge=1, le=100),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    return [experiments.validate_record(r) for r in experiments.list_experiments(get_db(), notebook_id, user_id, limit)]


@app.get("/experiments/{experiment_id}", response_model=ExperimentRecord)
async def get_experiment(
    experiment_id: str,
    notebook_id: str = Query(...),
    user_id: str = Query(...),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    record = experiments.get_experiment(get_db(), experiment_id, notebook_id, user_id)
    if record is None:
        raise HTTPException(status_code=404, detail="Experiment not found")
    return experiments.validate_record(record)
