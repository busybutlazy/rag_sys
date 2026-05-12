from fastapi import FastAPI
from app.db import get_db

app = FastAPI(title="RAG Server", version="0.1.0")


@app.get("/health")
async def health():
    db = get_db()
    db.version()
    return {"status": "ok", "service": "rag-server"}
