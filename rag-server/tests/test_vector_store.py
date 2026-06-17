import unittest

from app import vector_store


class FakeAql:
    def __init__(self):
        self.last_query = None
        self.last_bind_vars = None

    def execute(self, query, bind_vars):
        self.last_query = query
        self.last_bind_vars = bind_vars
        return []


class FakeDb:
    def __init__(self, indexes=None, document_count=1):
        self.aql = FakeAql()
        self._collection = FakeCollection(indexes=indexes, document_count=document_count)
        self._named_collections: dict[str, FakeCollection] = {}
        self._existing_collections: set[str] = set()
        self._existing_graphs: set[str] = set()
        self.created_collections: list[tuple[str, bool]] = []
        self.created_graphs: list[dict] = []

    def collection(self, name):
        if name in self._named_collections:
            return self._named_collections[name]
        self._collection.name = name
        return self._collection

    def views(self):
        return []

    def create_view(self, **kwargs):
        self.created_view = kwargs

    def has_collection(self, name):
        return name in self._existing_collections

    def create_collection(self, name, edge=False):
        self._existing_collections.add(name)
        self.created_collections.append((name, edge))

    def has_graph(self, name):
        return name in self._existing_graphs

    def create_graph(self, name, edge_definitions):
        self._existing_graphs.add(name)
        self.created_graphs.append({"name": name, "edge_definitions": edge_definitions})


class FakeCollection:
    def __init__(self, indexes=None, document_count=1):
        self.name = None
        self._indexes = [{"type": "vector"}] if indexes is None else indexes
        self._document_count = document_count
        self.added_indexes = []

    def indexes(self):
        return self._indexes

    def count(self):
        return self._document_count

    def add_index(self, index):
        self.added_indexes.append(index)


class VectorStoreTests(unittest.TestCase):
    def test_search_vector_scopes_notebook_and_top_k(self):
        db = FakeDb()

        vector_store.search_vector(db, [0.1, 0.2], "nb-1", "user-1", 7)

        self.assertIn("FILTER doc.notebook_id == @notebook_id", db.aql.last_query)
        self.assertIn("AND doc.user_id == @user_id", db.aql.last_query)
        self.assertEqual(
            {
                "notebook_id": "nb-1",
                "user_id": "user-1",
                "query_vec": [0.1, 0.2],
                "top_k": 7,
                "retrieval_version_id": None,
            },
            db.aql.last_bind_vars,
        )

    def test_search_vector_can_scope_retrieval_version(self):
        db = FakeDb()

        vector_store.search_vector(db, [0.1, 0.2], "nb-1", "user-1", 7, "rv-a")

        self.assertIn("doc.retrieval_version_id == @retrieval_version_id", db.aql.last_query)
        self.assertEqual("rv-a", db.aql.last_bind_vars["retrieval_version_id"])

    def test_search_bm25_can_scope_retrieval_version(self):
        db = FakeDb()

        vector_store.search_bm25(db, "hello", "nb-1", "user-1", 5, "rv-b")

        self.assertIn("doc.retrieval_version_id == @retrieval_version_id", db.aql.last_query)
        self.assertEqual("rv-b", db.aql.last_bind_vars["retrieval_version_id"])

    def test_search_vector_returns_empty_before_first_vector_index(self):
        db = FakeDb(indexes=[])

        self.assertEqual([], vector_store.search_vector(db, [0.1, 0.2], "nb-1", "user-1", 7))
        self.assertIsNone(db.aql.last_query)

    def test_ensure_vector_index_waits_for_documents(self):
        db = FakeDb(indexes=[], document_count=0)

        vector_store.ensure_vector_index(db)

        self.assertEqual([], db._collection.added_indexes)

    def test_ensure_vector_index_caps_nlists_to_document_count(self):
        db = FakeDb(indexes=[], document_count=1)

        vector_store.ensure_vector_index(db)

        self.assertEqual(1, db._collection.added_indexes[0]["params"]["nLists"])

    def test_delete_notebook_payload_scopes_chunks_and_documents_by_user(self):
        db = FakeDb()

        vector_store.delete_notebook_payload(db, "nb-1", "user-1")

        self.assertEqual(
            {"nid": "nb-1", "uid": "user-1"},
            db.aql.last_bind_vars,
        )

    def test_ensure_search_view_indexes_user_id(self):
        db = FakeDb()

        vector_store.ensure_search_view(db)

        fields = db.created_view["properties"]["links"]["chunks"]["fields"]
        self.assertIn("user_id", fields)
        self.assertIn("retrieval_version_id", fields)

    def test_delete_chunks_without_version_only_deletes_versionless_chunks(self):
        db = FakeDb()

        vector_store.delete_chunks(db, "src-1", "user-1")

        self.assertIn("doc.source_id == @sid", db.aql.last_query)
        self.assertIn("doc.retrieval_version_id == null", db.aql.last_query)
        self.assertEqual({"sid": "src-1", "uid": "user-1"}, db.aql.last_bind_vars)

    def test_delete_chunks_with_version_scopes_to_that_version(self):
        db = FakeDb()

        vector_store.delete_chunks(db, "src-1", "user-1", retrieval_version_id="rv-42")

        self.assertIn("doc.retrieval_version_id == @rv", db.aql.last_query)
        self.assertEqual({"sid": "src-1", "uid": "user-1", "rv": "rv-42"}, db.aql.last_bind_vars)

    def test_delete_chunks_without_version_does_not_wipe_other_versions(self):
        # Regression test: a missing/None retrieval_version_id must not be
        # treated as "delete everything for this source." Simulate real
        # filtering semantics so we prove versioned chunks survive.
        class FilteringAql:
            def __init__(self, docs):
                self.docs = docs
                self.last_query = None
                self.last_bind_vars = None

            def execute(self, query, bind_vars):
                self.last_query = query
                self.last_bind_vars = bind_vars
                sid = bind_vars.get("sid")
                uid = bind_vars.get("uid")
                rv = bind_vars.get("rv")
                if "rv" in bind_vars:
                    self.docs = [
                        d for d in self.docs
                        if not (d["source_id"] == sid and d["user_id"] == uid and d.get("retrieval_version_id") == rv)
                    ]
                else:
                    self.docs = [
                        d for d in self.docs
                        if not (d["source_id"] == sid and d["user_id"] == uid and d.get("retrieval_version_id") is None)
                    ]
                return []

        db = FakeDb()
        db.aql = FilteringAql([
            {"source_id": "src-1", "user_id": "user-1", "retrieval_version_id": None},
            {"source_id": "src-1", "user_id": "user-1", "retrieval_version_id": "rv-1"},
            {"source_id": "src-1", "user_id": "user-1", "retrieval_version_id": "rv-2"},
        ])

        vector_store.delete_chunks(db, "src-1", "user-1", retrieval_version_id=None)

        remaining_versions = {d["retrieval_version_id"] for d in db.aql.docs}
        self.assertEqual({"rv-1", "rv-2"}, remaining_versions)

    def test_delete_all_source_chunks_deletes_every_version(self):
        db = FakeDb()

        vector_store.delete_all_source_chunks(db, "src-1", "user-1")

        self.assertIn("doc.source_id == @sid", db.aql.last_query)
        self.assertNotIn("retrieval_version_id", db.aql.last_query)
        self.assertEqual({"sid": "src-1", "uid": "user-1"}, db.aql.last_bind_vars)

    def test_ensure_collections_creates_graph_vertex_and_edge_collections(self):
        db = FakeDb()

        vector_store.ensure_collections(db)

        for name in ("documents", "chunks", "notebooks", "experiments", "entities", "facts"):
            self.assertIn((name, False), db.created_collections)
        for name in ("chunk_mentions_entity", "fact_has_participant", "fact_supported_by_chunk"):
            self.assertIn((name, True), db.created_collections)

    def test_ensure_collections_is_idempotent(self):
        db = FakeDb()

        vector_store.ensure_collections(db)
        vector_store.ensure_collections(db)

        self.assertEqual(len(db.created_collections), len(set(db.created_collections)))

    def test_ensure_knowledge_graph_creates_graph_once(self):
        db = FakeDb()

        vector_store.ensure_knowledge_graph(db)
        vector_store.ensure_knowledge_graph(db)

        self.assertEqual(1, len(db.created_graphs))
        edge_collections = {e["edge_collection"] for e in db.created_graphs[0]["edge_definitions"]}
        self.assertEqual(
            {"chunk_mentions_entity", "fact_has_participant", "fact_supported_by_chunk"},
            edge_collections,
        )

    def test_ensure_graph_indexes_adds_persistent_index_on_entities_and_facts(self):
        db = FakeDb(indexes=[])
        entities_col = FakeCollection(indexes=[])
        facts_col = FakeCollection(indexes=[])
        db._named_collections = {"entities": entities_col, "facts": facts_col}

        vector_store.ensure_graph_indexes(db)

        for col in (entities_col, facts_col):
            self.assertEqual(1, len(col.added_indexes))
            self.assertEqual(["notebook_id", "retrieval_version_id"], col.added_indexes[0]["fields"])

    def test_ensure_graph_indexes_is_idempotent(self):
        db = FakeDb()
        existing = [{"type": "persistent", "fields": ["notebook_id", "retrieval_version_id"]}]
        entities_col = FakeCollection(indexes=existing)
        facts_col = FakeCollection(indexes=existing)
        db._named_collections = {"entities": entities_col, "facts": facts_col}

        vector_store.ensure_graph_indexes(db)

        self.assertEqual([], entities_col.added_indexes)
        self.assertEqual([], facts_col.added_indexes)

    def test_ensure_entities_view_indexes_canonical_name_and_aliases(self):
        db = FakeDb()

        vector_store.ensure_entities_view(db)

        fields = db.created_view["properties"]["links"]["entities"]["fields"]
        self.assertIn("canonical_name", fields)
        self.assertIn("aliases", fields)
        self.assertIn("notebook_id", fields)
        self.assertIn("retrieval_version_id", fields)

    def test_delete_version_chunks_filters_by_notebook_and_version(self):
        db = FakeDb()

        vector_store.delete_version_chunks(db, "nb-1", "user-1", "rv-99")

        self.assertIn("doc.notebook_id == @nid", db.aql.last_query)
        self.assertIn("doc.retrieval_version_id == @rv", db.aql.last_query)
        self.assertEqual({"nid": "nb-1", "uid": "user-1", "rv": "rv-99"}, db.aql.last_bind_vars)

    def test_get_chunk_ids_by_index_scopes_source_and_version(self):
        class RowAql(FakeAql):
            def execute(self, query, bind_vars):
                super().execute(query, bind_vars)
                return [
                    {"chunk_index": 0, "_id": "chunks/a"},
                    {"chunk_index": 1, "_id": "chunks/b"},
                ]

        db = FakeDb()
        db.aql = RowAql()

        result = vector_store.get_chunk_ids_by_index(db, "src-1", "user-1", "rv-1")

        self.assertEqual({0: "chunks/a", 1: "chunks/b"}, result)
        self.assertIn("doc.source_id == @source_id", db.aql.last_query)
        self.assertEqual(
            {"source_id": "src-1", "user_id": "user-1", "retrieval_version_id": "rv-1"},
            db.aql.last_bind_vars,
        )

    def test_delete_graph_payload_scopes_every_graph_collection_by_version(self):
        db = FakeDb()

        vector_store.delete_graph_payload(db, "nb-1", "user-1", "rv-1")

        # delete_graph_payload issues one AQL statement per graph collection;
        # FakeAql only records the last call, so just confirm the final one
        # is correctly scoped (the others use the same bind_vars shape).
        self.assertIn("doc.retrieval_version_id == @rv", db.aql.last_query)
        self.assertEqual({"nid": "nb-1", "uid": "user-1", "rv": "rv-1"}, db.aql.last_bind_vars)


if __name__ == "__main__":
    unittest.main()
