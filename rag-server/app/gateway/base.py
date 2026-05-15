from abc import ABC, abstractmethod


class EmbeddingGateway(ABC):
    dimensions: int
    model: str

    @abstractmethod
    async def embed(self, text: str) -> list[float]: ...

    @abstractmethod
    async def embed_batch(self, texts: list[str]) -> list[list[float]]: ...
