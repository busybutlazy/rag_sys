import os
from typing import Any

import httpx

_BE_URL = os.environ.get("BE_SERVER_URL", "http://be-server:8001")
_SECRET = os.environ.get("AI_INTERNAL_SECRET") or os.environ.get("INTERNAL_SECRET", "")
_TIMEOUT = 15.0


def _headers(authorization: str | None) -> dict[str, str]:
    return {"Authorization": authorization} if authorization else {}


async def list_notebooks(authorization: str | None) -> list[dict]:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.get(f"{_BE_URL}/api/notebooks", headers=_headers(authorization))
        res.raise_for_status()
        return res.json()


async def create_note(
    authorization: str | None,
    notebook_id: str,
    title: str | None,
    content: str,
) -> dict:
    async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
        res = await client.post(
            f"{_BE_URL}/api/notebooks/{notebook_id}/notes",
            headers=_headers(authorization),
            json={"title": title, "content": content},
        )
        res.raise_for_status()
        return res.json()


async def log_request(
    *,
    chat_request_id: str | None,
    session_id: str | None,
    service: str,
    operation: str,
    method: str | None = None,
    url: str | None = None,
    request_json: Any = None,
    response_json: Any = None,
    status_code: int | None = None,
    duration_ms: int | None = None,
    error: str | None = None,
    correlation_id: str | None = None,
) -> None:
    if not _SECRET:
        return
    payload = {
        "chat_request_id": chat_request_id,
        "session_id": session_id,
        "direction": "outbound",
        "service": service,
        "operation": operation,
        "method": method,
        "url": url,
        "request_json": request_json,
        "response_json": response_json,
        "status_code": status_code,
        "duration_ms": duration_ms,
        "error": error,
    }
    headers: dict[str, str] = {"X-Internal-Secret": _SECRET}
    if correlation_id:
        headers["X-Correlation-Id"] = correlation_id
    try:
        async with httpx.AsyncClient(timeout=_TIMEOUT) as client:
            await client.post(
                f"{_BE_URL}/internal/request-logs",
                headers=headers,
                json=payload,
            )
    except Exception:
        return
