import os
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException, Header, Query
from app.db import get_db
from app.models import IngestRequest, SearchResponse
from app import chunker, embedder, vector_store

_INTERNAL_SECRET = os.environ.get("INTERNAL_SECRET", "")


def _check_secret(x_internal_secret: str | None) -> None:
    if _INTERNAL_SECRET and x_internal_secret != _INTERNAL_SECRET:
        raise HTTPException(status_code=403, detail="Forbidden")


@asynccontextmanager
async def lifespan(_: FastAPI):
    vector_store.ensure_vector_index(get_db())
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
    documents = db.collection("chunks")  # use chunks collection for doc tracking too? No — keep documents collection
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
