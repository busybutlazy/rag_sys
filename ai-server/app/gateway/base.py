from abc import ABC, abstractmethod
from typing import AsyncGenerator


class GatewayError(Exception):
    def __init__(self, message: str, retryable: bool = False):
        super().__init__(message)
        self.retryable = retryable


class LLMGateway(ABC):
    @abstractmethod
    async def stream_complete(
        self,
        messages: list[dict],
        model: str,
    ) -> AsyncGenerator[str, None]:
        """Yield text tokens as they arrive from the model."""
        ...

    @abstractmethod
    async def complete_structured(
        self,
        messages: list[dict],
        schema: dict,
        model: str,
    ) -> dict:
        """Return a structured JSON response validated against schema."""
        ...
