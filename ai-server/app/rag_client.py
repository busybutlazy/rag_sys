import os
import httpx

_RAG_URL = os.environ.get("RAG_SERVER_URL", "http://rag-server:8003")
_SECRET = os.environ.get("RAG_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
_TIMEOUT = 15.0


def _headers(correlation_id: str | None = None) -> dict[str, str]:
    h = {"X-Internal-Secret": _SECRET}
    if correlation_id:
        h["X-Correlation-Id"] = correlation_id
    return h


async def search(
    query: str,
    notebook_id: str,
    user_id: str,
    top_k: int = 5,
    correlation_id: str | None = None,
) -> list[dict]:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.get(
            f"{_RAG_URL}/search/hybrid",
            params={"q": query, "notebook_id": notebook_id, "user_id": user_id, "top_k": top_k},
            headers=_headers(correlation_id),
        )
        res.raise_for_status()
        return res.json()["results"]


async def get_source_content(
    source_id: str,
    notebook_id: str,
    user_id: str,
    max_chars: int = 12000,
    correlation_id: str | None = None,
) -> dict:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.get(
            f"{_RAG_URL}/documents/{source_id}/content",
            params={"notebook_id": notebook_id, "user_id": user_id, "max_chars": max_chars},
            headers=_headers(correlation_id),
        )
        res.raise_for_status()
        return res.json()
