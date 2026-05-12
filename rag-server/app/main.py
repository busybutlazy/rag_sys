import os
from fastapi import FastAPI, HTTPException
from app.db import get_db
from app.models import IngestRequest

app = FastAPI(title="RAG Server", version="0.1.0")


@app.get("/health")
async def health():
    db = get_db()
    db.version()
    return {"status": "ok", "service": "rag-server"}


@app.post("/ingest", status_code=202)
async def ingest(req: IngestRequest):
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
