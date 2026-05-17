import json
from typing import AsyncGenerator

import openai
from openai import AsyncOpenAI

from app.gateway.base import GatewayError, LLMGateway


class OpenAIGateway(LLMGateway):
    def __init__(self, api_key: str):
        self._client = AsyncOpenAI(api_key=api_key)

    async def stream_complete(
        self,
        messages: list[dict],
        model: str,
    ) -> AsyncGenerator[str, None]:
        try:
            stream = await self._client.chat.completions.create(
                model=model,
                messages=messages,
                stream=True,
            )
            async for chunk in stream:
                delta = chunk.choices[0].delta.content
                if delta:
                    yield delta
        except openai.RateLimitError as exc:
            raise GatewayError(str(exc), retryable=True) from exc
        except openai.APITimeoutError as exc:
            raise GatewayError(str(exc), retryable=True) from exc
        except openai.APIError as exc:
            raise GatewayError(str(exc), retryable=False) from exc

    async def complete_structured(
        self,
        messages: list[dict],
        schema: dict,
        model: str,
    ) -> dict:
        try:
            response = await self._client.chat.completions.create(
                model=model,
                messages=messages,
                response_format={"type": "json_schema", "json_schema": schema},
            )
            content = response.choices[0].message.content or "{}"
            return json.loads(content)
        except openai.RateLimitError as exc:
            raise GatewayError(str(exc), retryable=True) from exc
        except openai.APITimeoutError as exc:
            raise GatewayError(str(exc), retryable=True) from exc
        except openai.APIError as exc:
            raise GatewayError(str(exc), retryable=False) from exc
