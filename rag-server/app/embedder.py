import os
from openai import AsyncOpenAI

_MODEL = "text-embedding-3-small"
_DIMENSIONS = 1536
_BATCH_SIZE = 100

_client: AsyncOpenAI | None = None


def _get_client() -> AsyncOpenAI:
    global _client
    if _client is None:
        _client = AsyncOpenAI(api_key=os.environ.get("OPENAI_API_KEY", ""))
    return _client


async def embed(text: str) -> list[float]:
    res = await _get_client().embeddings.create(input=[text], model=_MODEL)
    return res.data[0].embedding


async def embed_batch(texts: list[str]) -> list[list[float]]:
    results: list[list[float]] = []
    for i in range(0, len(texts), _BATCH_SIZE):
        batch = texts[i : i + _BATCH_SIZE]
        res = await _get_client().embeddings.create(input=batch, model=_MODEL)
        results.extend(item.embedding for item in res.data)
    return results


DIMENSIONS = _DIMENSIONS
