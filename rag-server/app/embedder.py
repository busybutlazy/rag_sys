from app.gateway.openai_embedding import default_gateway as _make_gateway

_gateway = _make_gateway()

DIMENSIONS: int = _gateway.dimensions


async def embed(text: str) -> list[float]:
    return await _gateway.embed(text)


async def embed_batch(texts: list[str]) -> list[list[float]]:
    return await _gateway.embed_batch(texts)
