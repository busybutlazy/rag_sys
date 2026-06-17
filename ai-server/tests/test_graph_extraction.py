import asyncio
import os
import unittest

os.environ.setdefault("OPENAI_API_KEY", "test")

from app.gateway.base import GatewayError, LLMGateway
from app.graph_extraction import extract_chunk, extract_graph


class FakeGateway(LLMGateway):
    def __init__(self, responses=None, error=None):
        self._responses = responses or []
        self._error = error
        self.calls: list[dict] = []

    async def stream_complete(self, messages, model):
        raise NotImplementedError

    async def complete_structured(self, messages, schema, model):
        self.calls.append({"messages": messages, "schema": schema, "model": model})
        if self._error:
            raise self._error
        return self._responses.pop(0)


class ExtractChunkTests(unittest.TestCase):
    def test_returns_mentions_and_facts_from_gateway(self):
        gateway = FakeGateway(
            responses=[
                {
                    "mentions": [{"entity_name": "Ada Lovelace", "entity_type": "person"}],
                    "facts": [
                        {
                            "predicate": "wrote",
                            "statement_text": "Ada Lovelace wrote notes on the Analytical Engine.",
                            "confidence": 0.9,
                            "participants": [{"entity_name": "Ada Lovelace", "role": "subject"}],
                        }
                    ],
                }
            ]
        )

        result = asyncio.run(extract_chunk(gateway, "Ada Lovelace wrote notes...", "gpt-4o-mini"))

        self.assertEqual(1, len(result["mentions"]))
        self.assertEqual(1, len(result["facts"]))
        self.assertEqual("gpt-4o-mini", gateway.calls[0]["model"])

    def test_missing_keys_default_to_empty_lists(self):
        gateway = FakeGateway(responses=[{}])

        result = asyncio.run(extract_chunk(gateway, "no structured fields", "gpt-4o-mini"))

        self.assertEqual({"mentions": [], "facts": []}, result)

    def test_gateway_error_degrades_to_empty_result_instead_of_raising(self):
        gateway = FakeGateway(error=GatewayError("rate limited", retryable=True))

        result = asyncio.run(extract_chunk(gateway, "anything", "gpt-4o-mini"))

        self.assertEqual({"mentions": [], "facts": []}, result)


class ExtractGraphTests(unittest.TestCase):
    def test_preserves_chunk_index_ordering_and_isolates_per_chunk_failures(self):
        gateway = FakeGateway(
            responses=[
                {"mentions": [{"entity_name": "A", "entity_type": "x"}], "facts": []},
            ]
        )
        # Second chunk's gateway call raises; simulate by swapping in an
        # error after the first successful response is consumed.
        original_complete_structured = gateway.complete_structured

        call_count = {"n": 0}

        async def flaky_complete_structured(messages, schema, model):
            call_count["n"] += 1
            if call_count["n"] == 2:
                raise GatewayError("boom")
            return await original_complete_structured(messages, schema, model)

        gateway.complete_structured = flaky_complete_structured

        results = asyncio.run(
            extract_graph(
                gateway,
                [
                    {"chunk_index": 0, "text": "first chunk"},
                    {"chunk_index": 1, "text": "second chunk"},
                ],
                "gpt-4o-mini",
            )
        )

        self.assertEqual([0, 1], [r["chunk_index"] for r in results])
        self.assertEqual(1, len(results[0]["mentions"]))
        self.assertEqual({"mentions": [], "facts": []}, {"mentions": results[1]["mentions"], "facts": results[1]["facts"]})


if __name__ == "__main__":
    unittest.main()
