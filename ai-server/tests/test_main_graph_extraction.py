import os
import unittest

os.environ.setdefault("OPENAI_API_KEY", "test")
os.environ.setdefault("JWT_SECRET", "test_jwt_secret_at_least_32_characters_long")
os.environ.setdefault("AI_INTERNAL_SECRET", "test_ai_secret_at_least_32_characters_long")
os.environ.setdefault("RAG_INTERNAL_SECRET", "test_rag_secret_at_least_32_characters_long")

from fastapi.testclient import TestClient

from app import main
from app.gateway.base import LLMGateway


class StubGateway(LLMGateway):
    def __init__(self, response):
        self._response = response

    async def stream_complete(self, messages, model):
        raise NotImplementedError

    async def complete_structured(self, messages, schema, model):
        return self._response


class ExtractGraphEndpointTests(unittest.TestCase):
    def setUp(self):
        self._original_gateway = main._gateway
        main._gateway = StubGateway(
            {
                "mentions": [{"entity_name": "Ada Lovelace", "entity_type": "person"}],
                "facts": [],
            }
        )
        self.client = TestClient(main.app)

    def tearDown(self):
        main._gateway = self._original_gateway

    def test_rejects_request_without_internal_secret(self):
        response = self.client.post(
            "/ai/extract/graph",
            json={"chunks": [{"chunk_index": 0, "text": "hello"}]},
        )
        self.assertEqual(401, response.status_code)

    def test_rejects_disallowed_model(self):
        response = self.client.post(
            "/ai/extract/graph",
            headers={"X-Internal-Secret": main._AI_INTERNAL_SECRET},
            json={"chunks": [{"chunk_index": 0, "text": "hello"}], "model": "not-a-real-model"},
        )
        self.assertEqual(422, response.status_code)

    def test_extracts_mentions_for_each_chunk_preserving_index(self):
        response = self.client.post(
            "/ai/extract/graph",
            headers={"X-Internal-Secret": main._AI_INTERNAL_SECRET},
            json={
                "chunks": [
                    {"chunk_index": 0, "text": "Ada Lovelace wrote notes."},
                    {"chunk_index": 1, "text": "She worked with Babbage."},
                ]
            },
        )

        self.assertEqual(200, response.status_code)
        body = response.json()
        self.assertEqual([0, 1], [item["chunk_index"] for item in body])
        self.assertEqual(1, len(body[0]["mentions"]))


if __name__ == "__main__":
    unittest.main()
