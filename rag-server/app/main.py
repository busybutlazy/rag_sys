import os
from fastapi import FastAPI, HTTPException, Header
from app.db import get_db
from app.models import IngestRequest

app = FastAPI(title="RAG Server", version="0.1.0")

# SEC-04: simple shared secret between be-server and rag-server (internal network only)
_INTERNAL_SECRET = os.environ.get("INTERNAL_SECRET", "")


def _check_secret(x_internal_secret: str | None):
    if _INTERNAL_SECRET and x_internal_secret != _INTERNAL_SECRET:
        raise HTTPException(status_code=403, detail="Forbidden")


@app.get("/health")
async def health():
    db = get_db()
    db.version()
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
    documents = db.collection("documents")

    doc = {
        "_key": req.source_id,
        "source_id": req.source_id,
        "file_path": req.file_path,
        "mime_type": req.mime_type,
        "status": "stored",
    }

    if documents.has(req.source_id):
        documents.update(doc)
    else:
        documents.insert(doc)

    return {"source_id": req.source_id, "status": "stored"}


@app.delete("/documents/{source_id}", status_code=204)
async def delete_document(
    source_id: str,
    x_internal_secret: str | None = Header(default=None),
):
    _check_secret(x_internal_secret)
    db = get_db()
    documents = db.collection("documents")
    if documents.has(source_id):
        documents.delete(source_id)
