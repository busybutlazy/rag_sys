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

    def collection(self, name):
        self._collection.name = name
        return self._collection

    def views(self):
        return []

    def create_view(self, **kwargs):
        self.created_view = kwargs


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
            {"notebook_id": "nb-1", "user_id": "user-1", "query_vec": [0.1, 0.2], "top_k": 7},
            db.aql.last_bind_vars,
        )

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

    def test_delete_chunks_without_version_deletes_all_for_source(self):
        db = FakeDb()

        vector_store.delete_chunks(db, "src-1", "user-1")

        self.assertIn("doc.source_id == @sid", db.aql.last_query)
        self.assertNotIn("retrieval_version_id", db.aql.last_query)
        self.assertEqual({"sid": "src-1", "uid": "user-1"}, db.aql.last_bind_vars)

    def test_delete_chunks_with_version_scopes_to_that_version(self):
        db = FakeDb()

        vector_store.delete_chunks(db, "src-1", "user-1", retrieval_version_id="rv-42")

        self.assertIn("doc.retrieval_version_id == @rv", db.aql.last_query)
        self.assertEqual({"sid": "src-1", "uid": "user-1", "rv": "rv-42"}, db.aql.last_bind_vars)

    def test_delete_version_chunks_filters_by_notebook_and_version(self):
        db = FakeDb()

        vector_store.delete_version_chunks(db, "nb-1", "user-1", "rv-99")

        self.assertIn("doc.notebook_id == @nid", db.aql.last_query)
        self.assertIn("doc.retrieval_version_id == @rv", db.aql.last_query)
        self.assertEqual({"nid": "nb-1", "uid": "user-1", "rv": "rv-99"}, db.aql.last_bind_vars)


if __name__ == "__main__":
    unittest.main()
