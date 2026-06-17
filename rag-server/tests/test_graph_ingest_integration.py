import os
import unittest
import uuid

from app.db import get_db
from app import graph_ingest, vector_store

_HAS_LIVE_ARANGO = bool(os.environ.get("ARANGO_URL"))


@unittest.skipUnless(
    _HAS_LIVE_ARANGO,
    "requires a live ArangoDB; set ARANGO_URL/ARANGO_DB/ARANGO_USER/ARANGO_PASSWORD "
    "(see docker compose up -d arangodb arango-init) to run this against the real database",
)
class GraphIngestIntegrationTests(unittest.TestCase):
    """End-to-end proof that resolve_and_assemble's writes are accepted by a
    real ArangoDB and traversable through the named graph -- not just
    structurally correct dicts handed to a fake collection.
    """

    @classmethod
    def setUpClass(cls):
        cls.db = get_db()
        vector_store.ensure_collections(cls.db)
        vector_store.ensure_knowledge_graph(cls.db)
        vector_store.ensure_graph_indexes(cls.db)
        vector_store.ensure_entities_view(cls.db)

        cls.notebook_id = f"test-nb-{uuid.uuid4().hex[:8]}"
        cls.user_id = f"test-user-{uuid.uuid4().hex[:8]}"
        cls.source_id = f"test-src-{uuid.uuid4().hex[:8]}"
        cls.retrieval_version_id = f"test-rv-{uuid.uuid4().hex[:8]}"

        chunks_col = cls.db.collection("chunks")
        cls.chunk_key = uuid.uuid4().hex
        chunks_col.insert(
            {
                "_key": cls.chunk_key,
                "source_id": cls.source_id,
                "notebook_id": cls.notebook_id,
                "user_id": cls.user_id,
                "retrieval_version_id": cls.retrieval_version_id,
                "chunk_index": 0,
                "text": "Ada Lovelace wrote notes on the Analytical Engine.",
            }
        )

        cls.extraction = [
            {
                "chunk_index": 0,
                "mentions": [
                    {"entity_name": "Ada Lovelace", "entity_type": "person"},
                    {"entity_name": "Analytical Engine", "entity_type": "concept"},
                ],
                "facts": [
                    {
                        "predicate": "wrote_notes_on",
                        "statement_text": "Ada Lovelace wrote notes on the Analytical Engine.",
                        "confidence": 0.95,
                        "participants": [
                            {"entity_name": "Ada Lovelace", "role": "subject"},
                            {"entity_name": "Analytical Engine", "role": "object"},
                        ],
                    }
                ],
            }
        ]

        cls.result = graph_ingest.resolve_and_assemble(
            cls.db, cls.source_id, cls.notebook_id, cls.user_id, cls.retrieval_version_id, cls.extraction
        )

    @classmethod
    def tearDownClass(cls):
        vector_store.delete_graph_payload(cls.db, cls.notebook_id, cls.user_id, cls.retrieval_version_id)
        cls.db.collection("chunks").delete(cls.chunk_key)

    def test_writes_no_skipped_chunks(self):
        self.assertEqual([], self.result["skipped_chunks"])
        self.assertEqual(2, self.result["entities_written"])
        self.assertEqual(1, self.result["facts_written"])

    def test_entity_documents_are_readable_with_expected_fields(self):
        entity_key = graph_ingest.entity_key(self.notebook_id, self.retrieval_version_id, "ada lovelace")
        doc = self.db.collection("entities").get(entity_key)
        self.assertIsNotNone(doc)
        self.assertEqual("ada lovelace", doc["canonical_name"])
        self.assertEqual(self.notebook_id, doc["notebook_id"])
        self.assertEqual(self.retrieval_version_id, doc["retrieval_version_id"])

    def test_graph_traversal_from_chunk_reaches_entity_and_fact(self):
        cursor = self.db.aql.execute(
            """
            FOR v, e IN 1..1 OUTBOUND @chunk_id chunk_mentions_entity
              RETURN v.canonical_name
            """,
            bind_vars={"chunk_id": f"chunks/{self.chunk_key}"},
        )
        reached_entities = set(cursor)
        self.assertEqual({"ada lovelace", "analytical engine"}, reached_entities)

    def test_fact_traversal_reaches_both_participants_and_source_chunk(self):
        fact_key = graph_ingest.fact_key(
            self.notebook_id, self.retrieval_version_id, 0, 0, "wrote_notes_on"
        )
        participants = list(
            self.db.aql.execute(
                """
                FOR v, e IN 1..1 OUTBOUND @fact_id fact_has_participant
                  RETURN { name: v.canonical_name, role: e.role }
                """,
                bind_vars={"fact_id": f"facts/{fact_key}"},
            )
        )
        self.assertEqual(
            {("ada lovelace", "subject"), ("analytical engine", "object")},
            {(p["name"], p["role"]) for p in participants},
        )

        supporting_chunks = list(
            self.db.aql.execute(
                """
                FOR v, e IN 1..1 OUTBOUND @fact_id fact_supported_by_chunk
                  RETURN v._key
                """,
                bind_vars={"fact_id": f"facts/{fact_key}"},
            )
        )
        self.assertEqual([self.chunk_key], supporting_chunks)

    def test_rerunning_ingest_is_idempotent_not_duplicative(self):
        graph_ingest.resolve_and_assemble(
            self.db, self.source_id, self.notebook_id, self.user_id, self.retrieval_version_id, self.extraction
        )

        entity_key = graph_ingest.entity_key(self.notebook_id, self.retrieval_version_id, "ada lovelace")
        doc = self.db.collection("entities").get(entity_key)
        # "Ada Lovelace" is counted twice per run (once as a mention, once as
        # a fact participant). Re-running with the same extraction should
        # overwrite back to that same per-run count of 2, not accumulate to
        # 4+ across repeated calls -- that's the idempotency being tested.
        self.assertEqual(2, doc["mention_count"])


if __name__ == "__main__":
    unittest.main()
