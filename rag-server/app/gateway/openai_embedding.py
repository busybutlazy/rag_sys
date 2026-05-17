import os

from openai import AsyncOpenAI

from app.gateway.base import EmbeddingGateway

_BATCH_SIZE = 100


class OpenAIEmbeddingGateway(EmbeddingGateway):
    def __init__(self, api_key: str, model: str, dimensions: int):
        self._client = AsyncOpenAI(api_key=api_key)
        self.model = model
        self.dimensions = dimensions

    async def embed(self, text: str) -> list[float]:
        res = await self._client.embeddings.create(input=[text], model=self.model)
        return res.data[0].embedding

    async def embed_batch(self, texts: list[str]) -> list[list[float]]:
        results: list[list[float]] = []
        for i in range(0, len(texts), _BATCH_SIZE):
            batch = texts[i : i + _BATCH_SIZE]
            res = await self._client.embeddings.create(input=batch, model=self.model)
            results.extend(item.embedding for item in res.data)
        return results


def default_gateway() -> OpenAIEmbeddingGateway:
    return OpenAIEmbeddingGateway(
        api_key=os.environ.get("OPENAI_API_KEY", ""),
        model=os.environ.get("EMBEDDING_MODEL", "text-embedding-3-small"),
        dimensions=int(os.environ.get("EMBEDDING_DIMENSIONS", "1536")),
    )
