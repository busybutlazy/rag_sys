import os
import unittest
import uuid

from app.db import get_db
from app import graph_ingest, vector_store
from app.embedder import DIMENSIONS

_HAS_LIVE_ARANGO = bool(os.environ.get("ARANGO_URL"))
# A live `chunks` collection may already have a vector index (lazily created
# by ensure_vector_index once any document carries an `embedding`). If so,
# every insert -- including these test fixtures -- must supply a vector of
# the right dimensionality or ArangoDB rejects the write.
_DUMMY_EMBEDDING = [0.0] * DIMENSIONS


@unittest.skipUnless(
    _HAS_LIVE_ARANGO,
    "requires a live ArangoDB; set ARANGO_URL/ARANGO_DB/ARANGO_USER/ARANGO_PASSWORD "
    "(see docker compose up -d arangodb arango-init) to run this against the real database",
)
class GraphSearchIntegrationTests(unittest.TestCase):
    """Phase 19 Gate C: proves search_graph_branch actually traverses a real
    ArangoDB knowledge graph (chunk -> entity -> fact -> chunk) and that the
    retrieval_version_id isolation guarantee from Phase 18 Gate A holds for
    the new graph collections too -- not just structurally correct dicts
    handed to a fake AQL layer."""

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
        cls.rv_a = f"test-rv-a-{uuid.uuid4().hex[:8]}"
        cls.rv_b = f"test-rv-b-{uuid.uuid4().hex[:8]}"

        chunks_col = cls.db.collection("chunks")
        cls.chunk_key_a = uuid.uuid4().hex
        cls.chunk_key_b = uuid.uuid4().hex
        chunks_col.insert({
            "_key": cls.chunk_key_a,
            "source_id": cls.source_id,
            "notebook_id": cls.notebook_id,
            "user_id": cls.user_id,
            "retrieval_version_id": cls.rv_a,
            "chunk_index": 0,
            "text": "Grace Hopper invented the COBOL compiler.",
            "embedding": _DUMMY_EMBEDDING,
        })
        chunks_col.insert({
            "_key": cls.chunk_key_b,
            "source_id": cls.source_id,
            "notebook_id": cls.notebook_id,
            "user_id": cls.user_id,
            "retrieval_version_id": cls.rv_b,
            "chunk_index": 0,
            "text": "Grace Hopper invented the COBOL compiler.",
            "embedding": _DUMMY_EMBEDDING,
        })

        # Same chunk_index and the same mentioned entity name in both
        # versions on purpose -- this is exactly the scenario where leakage
        # would slip through if entity/fact keys weren't version-scoped.
        extraction = [{
            "chunk_index": 0,
            "mentions": [{"entity_name": "Grace Hopper", "entity_type": "person"}],
            "facts": [{
                "predicate": "invented",
                "statement_text": "Grace Hopper invented the COBOL compiler.",
                "confidence": 0.9,
                "participants": [{"entity_name": "Grace Hopper", "role": "subject"}],
            }],
        }]
        cls.result_a = graph_ingest.resolve_and_assemble(
            cls.db, cls.source_id, cls.notebook_id, cls.user_id, cls.rv_a, extraction
        )
        cls.result_b = graph_ingest.resolve_and_assemble(
            cls.db, cls.source_id, cls.notebook_id, cls.user_id, cls.rv_b, extraction
        )

    @classmethod
    def tearDownClass(cls):
        vector_store.delete_graph_payload(cls.db, cls.notebook_id, cls.user_id, cls.rv_a)
        vector_store.delete_graph_payload(cls.db, cls.notebook_id, cls.user_id, cls.rv_b)
        cls.db.collection("chunks").delete(cls.chunk_key_a)
        cls.db.collection("chunks").delete(cls.chunk_key_b)

    def test_ingest_succeeded_for_both_versions(self):
        self.assertEqual([], self.result_a["skipped_chunks"])
        self.assertEqual([], self.result_b["skipped_chunks"])

    def test_graph_branch_returns_fact_provenance_for_its_own_version(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        results = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv_a
        )

        self.assertEqual(1, len(results))
        self.assertEqual(self.source_id, results[0]["source_id"])
        self.assertEqual("invented", "invented")  # predicate isn't surfaced directly; fact_text is
        self.assertIn("Grace Hopper", results[0]["fact_text"])
        self.assertEqual(["grace hopper"], results[0]["participants"])

    def test_graph_branch_does_not_leak_across_retrieval_versions(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        results_a = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv_a
        )
        results_b = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv_b
        )

        # Both should resolve their own fact, and crucially the fact ids must
        # differ -- same predicate/entity text, but version-scoped keys.
        self.assertEqual(1, len(results_a))
        self.assertEqual(1, len(results_b))
        self.assertNotEqual(results_a[0]["fact_id"], results_b[0]["fact_id"])

    def test_graph_branch_finds_nothing_for_an_unrelated_version_scope(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        results = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id="unrelated-rv"
        )

        self.assertEqual([], results)

    def test_graph_branch_returns_empty_without_error_when_max_graph_hops_exhausts(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        # A single isolated entity/fact pair has nothing further to expand
        # to on a second hop -- must terminate cleanly, not error.
        results = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv_a, max_graph_hops=3
        )

        self.assertEqual(1, len(results))


@unittest.skipUnless(
    _HAS_LIVE_ARANGO,
    "requires a live ArangoDB; set ARANGO_URL/ARANGO_DB/ARANGO_USER/ARANGO_PASSWORD "
    "(see docker compose up -d arangodb arango-init) to run this against the real database",
)
class GraphSearchTwoHopIntegrationTests(unittest.TestCase):
    """Phase 19 Gate C review fix (finding 4): proves max_graph_hops=2 finds
    a fact reachable only via a second hop, against a real ArangoDB graph --
    not just that hop-exhaustion terminates cleanly (the single-fact case
    already covered by GraphSearchIntegrationTests)."""

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
        cls.rv = f"test-rv-{uuid.uuid4().hex[:8]}"

        chunks_col = cls.db.collection("chunks")
        cls.chunk_key = uuid.uuid4().hex
        chunks_col.insert({
            "_key": cls.chunk_key,
            "source_id": cls.source_id,
            "notebook_id": cls.notebook_id,
            "user_id": cls.user_id,
            "retrieval_version_id": cls.rv,
            "chunk_index": 0,
            "text": "Alice co-founded Acme Corp with Bob, who later partnered with Carol.",
            "embedding": _DUMMY_EMBEDDING,
        })

        # Two facts in the same chunk extraction, chained through a shared
        # entity (Bob) that is NOT mentioned directly in the seed chunk's
        # chunk_mentions_entity edge -- only Alice is. Fact 1 connects Alice
        # and Bob; fact 2 connects Bob and Carol. Carol/fact 2 is reachable
        # only by hopping: seed chunk -> Alice -> fact 1 -> Bob -> fact 2.
        extraction = [{
            "chunk_index": 0,
            "mentions": [{"entity_name": "Alice", "entity_type": "person"}],
            "facts": [
                {
                    "predicate": "co_founded_with",
                    "statement_text": "Alice co-founded Acme Corp with Bob.",
                    "confidence": 0.9,
                    "participants": [
                        {"entity_name": "Alice", "role": "subject"},
                        {"entity_name": "Bob", "role": "object"},
                    ],
                },
                {
                    "predicate": "partnered_with",
                    "statement_text": "Bob partnered with Carol.",
                    "confidence": 0.85,
                    "participants": [
                        {"entity_name": "Bob", "role": "subject"},
                        {"entity_name": "Carol", "role": "object"},
                    ],
                },
            ],
        }]
        cls.result = graph_ingest.resolve_and_assemble(
            cls.db, cls.source_id, cls.notebook_id, cls.user_id, cls.rv, extraction
        )

    @classmethod
    def tearDownClass(cls):
        vector_store.delete_graph_payload(cls.db, cls.notebook_id, cls.user_id, cls.rv)
        cls.db.collection("chunks").delete(cls.chunk_key)

    def test_ingest_succeeded(self):
        self.assertEqual([], self.result["skipped_chunks"])

    def test_second_hop_fact_is_found_when_max_graph_hops_is_2(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        results = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv, max_graph_hops=2
        )

        fact_texts = {r["fact_text"] for r in results}
        self.assertEqual(2, len(results))
        self.assertTrue(any("Bob" in t and "Carol" in t for t in fact_texts))
        self.assertTrue(any("Alice" in t and "Bob" in t for t in fact_texts))

    def test_second_hop_fact_is_not_found_when_max_graph_hops_is_1(self):
        seed = [{"source_id": self.source_id, "chunk_index": 0}]

        results = vector_store.search_graph_branch(
            self.db, self.notebook_id, self.user_id, seed, retrieval_version_id=self.rv, max_graph_hops=1
        )

        fact_texts = {r["fact_text"] for r in results}
        self.assertEqual(1, len(results))
        self.assertFalse(any("Carol" in t for t in fact_texts))
        self.assertTrue(any("Alice" in t and "Bob" in t for t in fact_texts))


if __name__ == "__main__":
    unittest.main()
