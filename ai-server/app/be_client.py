import os

import httpx

_BE_URL = os.environ.get("BE_SERVER_URL", "http://be-server:8001")
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
