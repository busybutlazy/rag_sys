import os
import unittest

from app.db import get_db
from app import vector_store

_HAS_LIVE_ARANGO = bool(os.environ.get("ARANGO_URL"))


@unittest.skipUnless(
    _HAS_LIVE_ARANGO,
    "requires a live ArangoDB; set ARANGO_URL/ARANGO_DB/ARANGO_USER/ARANGO_PASSWORD "
    "(see docker compose up -d arangodb arango-init) to run this against the real database",
)
class VectorStoreGraphSchemaIntegrationTests(unittest.TestCase):
    """Verifies Phase 19 Gate A schema against a real ArangoDB instance.

    Unit tests in test_vector_store.py cover the same logic against a fake
    db client; this exists because fakes can't prove ArangoDB actually
    accepts the edge definitions, index specs, and view properties.
    """

    @classmethod
    def setUpClass(cls):
        cls.db = get_db()
        vector_store.ensure_collections(cls.db)
        vector_store.ensure_knowledge_graph(cls.db)
        vector_store.ensure_graph_indexes(cls.db)
        vector_store.ensure_entities_view(cls.db)

    def test_graph_vertex_and_edge_collections_exist(self):
        for name in ("entities", "facts"):
            self.assertTrue(self.db.has_collection(name), f"missing vertex collection: {name}")
        for name in ("chunk_mentions_entity", "fact_has_participant", "fact_supported_by_chunk"):
            self.assertTrue(self.db.has_collection(name), f"missing edge collection: {name}")

    def test_pre_existing_collections_untouched(self):
        for name in ("documents", "chunks", "notebooks", "experiments"):
            self.assertTrue(self.db.has_collection(name))

    def test_named_graph_joins_the_expected_edge_collections(self):
        self.assertTrue(self.db.has_graph(vector_store.KNOWLEDGE_GRAPH_NAME))
        graph = self.db.graph(vector_store.KNOWLEDGE_GRAPH_NAME)
        edge_collections = {e["edge_collection"] for e in graph.edge_definitions()}
        self.assertEqual(
            {"chunk_mentions_entity", "fact_has_participant", "fact_supported_by_chunk"},
            edge_collections,
        )

    def test_entities_and_facts_have_persistent_notebook_version_index(self):
        for name in ("entities", "facts"):
            fields = {tuple(idx.get("fields", [])) for idx in self.db.collection(name).indexes()}
            self.assertIn(("notebook_id", "retrieval_version_id"), fields)

    def test_entities_view_indexes_canonical_name_and_aliases(self):
        view_names = {v["name"] for v in self.db.views()}
        self.assertIn("entities_view", view_names)
        properties = self.db.view("entities_view")
        fields = properties["links"]["entities"]["fields"]
        self.assertIn("canonical_name", fields)
        self.assertIn("aliases", fields)

    def test_running_ensure_functions_twice_is_idempotent(self):
        vector_store.ensure_collections(self.db)
        vector_store.ensure_knowledge_graph(self.db)
        vector_store.ensure_graph_indexes(self.db)
        vector_store.ensure_entities_view(self.db)

        for name in ("entities", "facts"):
            self.assertTrue(self.db.has_collection(name))


if __name__ == "__main__":
    unittest.main()
