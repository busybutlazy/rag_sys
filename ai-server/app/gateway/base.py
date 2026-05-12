from abc import ABC, abstractmethod
from typing import AsyncGenerator


class LLMGateway(ABC):
    @abstractmethod
    async def stream_complete(
        self,
        messages: list[dict],
        model: str,
    ) -> AsyncGenerator[str, None]:
        """Yield text tokens as they arrive from the model."""
        ...
