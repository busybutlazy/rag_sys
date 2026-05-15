import os
import httpx

_RAG_URL = os.environ.get("RAG_SERVER_URL", "http://rag-server:8003")
_SECRET = os.environ.get("RAG_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
_TIMEOUT = 15.0


async def search(query: str, notebook_id: str, top_k: int = 5) -> list[dict]:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.get(
            f"{_RAG_URL}/search/hybrid",
            params={"q": query, "notebook_id": notebook_id, "top_k": top_k},
            headers={"X-Internal-Secret": _SECRET},
        )
        res.raise_for_status()
        return res.json()["results"]


async def get_source_content(source_id: str, notebook_id: str, max_chars: int = 12000) -> dict:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.get(
            f"{_RAG_URL}/documents/{source_id}/content",
            params={"notebook_id": notebook_id, "max_chars": max_chars},
            headers={"X-Internal-Secret": _SECRET},
        )
        res.raise_for_status()
        return res.json()
