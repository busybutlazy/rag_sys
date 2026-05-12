from typing import AsyncGenerator
from openai import AsyncOpenAI
from app.gateway.base import LLMGateway


class OpenAIGateway(LLMGateway):
    def __init__(self, api_key: str):
        self._client = AsyncOpenAI(api_key=api_key)

    async def stream_complete(
        self,
        messages: list[dict],
        model: str,
    ) -> AsyncGenerator[str, None]:
        stream = await self._client.chat.completions.create(
            model=model,
            messages=messages,
            stream=True,
        )
        async for chunk in stream:
            delta = chunk.choices[0].delta.content
            if delta:
                yield delta
