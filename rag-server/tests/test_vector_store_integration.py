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

    def _seed_graph_payload(self, notebook_id, user_id, source_id, retrieval_version_id):
        chunk_key = f"chunk-{notebook_id}-{retrieval_version_id}"
        self.db.collection("chunks").insert({
            "_key": chunk_key,
            "source_id": source_id,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
            "chunk_index": 0,
            "text": "seed",
        })
        entity_key = f"entity-{notebook_id}-{retrieval_version_id}"
        self.db.collection("entities").insert({
            "_key": entity_key,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
            "canonical_name": "seed entity",
        })
        fact_key = f"fact-{notebook_id}-{retrieval_version_id}"
        self.db.collection("facts").insert({
            "_key": fact_key,
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
            "predicate": "seed predicate",
        })
        self.db.collection("chunk_mentions_entity").insert({
            "_key": f"mention-{notebook_id}-{retrieval_version_id}",
            "_from": f"chunks/{chunk_key}",
            "_to": f"entities/{entity_key}",
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        })
        self.db.collection("fact_supported_by_chunk").insert({
            "_key": f"supports-{notebook_id}-{retrieval_version_id}",
            "_from": f"facts/{fact_key}",
            "_to": f"chunks/{chunk_key}",
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        })
        self.db.collection("fact_has_participant").insert({
            "_key": f"participant-{notebook_id}-{retrieval_version_id}",
            "_from": f"facts/{fact_key}",
            "_to": f"entities/{entity_key}",
            "role": "seed",
            "notebook_id": notebook_id,
            "user_id": user_id,
            "retrieval_version_id": retrieval_version_id,
        })

    def _graph_counts(self, notebook_id, user_id):
        counts = {}
        for name in (
            "entities",
            "facts",
            "chunk_mentions_entity",
            "fact_has_participant",
            "fact_supported_by_chunk",
        ):
            cursor = self.db.aql.execute(
                f"FOR doc IN {name} FILTER doc.notebook_id == @nid AND doc.user_id == @uid RETURN doc",
                bind_vars={"nid": notebook_id, "uid": user_id},
            )
            counts[name] = len(list(cursor))
        return counts

    def test_delete_all_notebook_graph_payload_clears_every_version(self):
        notebook_id = "it-nb-graph-wipe"
        user_id = "it-user-graph-wipe"
        self._seed_graph_payload(notebook_id, user_id, "src-a", "rv-1")
        self._seed_graph_payload(notebook_id, user_id, "src-a", "rv-2")

        vector_store.delete_all_notebook_graph_payload(self.db, notebook_id, user_id)

        counts = self._graph_counts(notebook_id, user_id)
        self.assertEqual({name: 0 for name in counts}, counts)

    def test_delete_all_user_graph_payload_clears_every_notebook(self):
        user_id = "it-user-graph-wipe-2"
        self._seed_graph_payload("it-nb-a", user_id, "src-a", "rv-1")
        self._seed_graph_payload("it-nb-b", user_id, "src-b", "rv-1")

        vector_store.delete_all_user_graph_payload(self.db, user_id)

        for notebook_id in ("it-nb-a", "it-nb-b"):
            counts = self._graph_counts(notebook_id, user_id)
            self.assertEqual({name: 0 for name in counts}, counts)

    def test_delete_source_graph_payload_removes_edges_for_that_source_only(self):
        notebook_id = "it-nb-source-wipe"
        user_id = "it-user-source-wipe"
        self._seed_graph_payload(notebook_id, user_id, "src-keep", "rv-1")
        self._seed_graph_payload(notebook_id, user_id, "src-remove", "rv-2")

        vector_store.delete_source_graph_payload(self.db, "src-remove", user_id)

        cursor = self.db.aql.execute(
            "FOR doc IN chunk_mentions_entity FILTER doc.notebook_id == @nid AND doc.user_id == @uid RETURN doc",
            bind_vars={"nid": notebook_id, "uid": user_id},
        )
        remaining_mentions = list(cursor)
        self.assertEqual(1, len(remaining_mentions))
        self.assertEqual("mention-it-nb-source-wipe-rv-1", remaining_mentions[0]["_key"])


if __name__ == "__main__":
    unittest.main()
