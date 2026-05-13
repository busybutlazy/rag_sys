import os
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException, Header, Query
from app.db import get_db
from app.models import IngestRequest, SearchResponse, BenchmarkResponse, SourceContentResponse
from app import chunker, embedder, vector_store

_INTERNAL_SECRET = os.environ.get("INTERNAL_SECRET", "")
if not _INTERNAL_SECRET:
    raise SystemExit("INTERNAL_SECRET must be set")


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


@app.get("/health")
async def health():
    get_db().version()
    return {"status": "ok", "service": "rag-server"}


@app.post("/ingest", status_code=200)
async def ingest(
    req: IngestRequest,
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)

    if not os.path.exists(req.file_path):
        raise HTTPException(status_code=404, detail=f"File not found: {req.file_path}")

    db = get_db()
    docs_col = db.collection("documents")

    # Upsert document record as "processing"
    doc_record = {
        "_key": req.source_id,
        "source_id": req.source_id,
        "notebook_id": req.notebook_id,
        "file_path": req.file_path,
        "mime_type": req.mime_type,
        "status": "processing",
    }
    if docs_col.has(req.source_id):
        docs_col.update(doc_record)
    else:
        docs_col.insert(doc_record)

    try:
        text = chunker.extract_text(req.file_path, req.mime_type)
        chunks = chunker.chunk_text(text)
        if not chunks:
            raise ValueError("No text extracted from file")

        embeddings = await embedder.embed_batch(chunks)

        # Remove old chunks for this source before inserting new ones
        vector_store.delete_chunks(db, req.source_id)
        vector_store.store_chunks(db, req.source_id, req.notebook_id, chunks, embeddings)

        docs_col.update({"_key": req.source_id, "status": "ready", "chunk_count": len(chunks)})
        return {"source_id": req.source_id, "status": "ready", "chunk_count": len(chunks)}

    except Exception as exc:
        docs_col.update({"_key": req.source_id, "status": "error", "error": str(exc)})
        raise HTTPException(status_code=500, detail=str(exc))


@app.get("/search/vector", response_model=SearchResponse)
async def search_vector(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    top_k: int = Query(default=5, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)

    query_embedding = await embedder.embed(q)
    results = vector_store.search_vector(get_db(), query_embedding, notebook_id, top_k)
    return SearchResponse(results=results)


@app.get("/search/bm25", response_model=SearchResponse)
async def search_bm25_endpoint(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    top_k: int = Query(default=5, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    results = vector_store.search_bm25(get_db(), q, notebook_id, top_k)
    return SearchResponse(results=results)


@app.get("/search/hybrid", response_model=SearchResponse)
async def search_hybrid_endpoint(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    top_k: int = Query(default=5, ge=1, le=20),
    alpha: float = Query(default=0.5, ge=0.0, le=1.0, description="Vector weight (1-alpha goes to BM25)"),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    query_embedding = await embedder.embed(q)
    results = vector_store.search_hybrid(get_db(), query_embedding, q, notebook_id, top_k, alpha)
    return SearchResponse(results=results)


@app.get("/search/benchmark", response_model=BenchmarkResponse)
async def search_benchmark(
    q: str = Query(..., description="Query text"),
    notebook_id: str = Query(..., description="Notebook to search within"),
    top_k: int = Query(default=5, ge=1, le=20),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    query_embedding = await embedder.embed(q)
    db = get_db()
    vec_results = vector_store.search_vector(db, query_embedding, notebook_id, top_k)
    bm25_results = vector_store.search_bm25(db, q, notebook_id, top_k)
    hybrid_results = vector_store.search_hybrid(db, query_embedding, q, notebook_id, top_k)
    return BenchmarkResponse(
        query=q,
        vector=vec_results,
        bm25=bm25_results,
        hybrid=hybrid_results,
    )


@app.delete("/documents/{source_id}", status_code=204)
async def delete_document(
    source_id: str,
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    db = get_db()
    vector_store.delete_chunks(db, source_id)
    docs_col = db.collection("documents")
    if docs_col.has(source_id):
        docs_col.delete(source_id)


@app.get("/documents/{source_id}/content", response_model=SourceContentResponse)
async def get_document_content(
    source_id: str,
    notebook_id: str = Query(..., description="Notebook scope for the source"),
    max_chars: int = Query(default=12000, ge=1000, le=50000),
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    content = vector_store.get_source_content(get_db(), source_id, notebook_id, max_chars)
    if not content["chunks"]:
        raise HTTPException(status_code=404, detail="Source content not found")
    return SourceContentResponse(**content)
