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
    def __init__(self):
        self.aql = FakeAql()


class VectorStoreTests(unittest.TestCase):
    def test_search_vector_scopes_notebook_and_top_k(self):
        db = FakeDb()

        vector_store.search_vector(db, [0.1, 0.2], "nb-1", 7)

        self.assertIn("FILTER doc.notebook_id == @notebook_id", db.aql.last_query)
        self.assertEqual(
            {"notebook_id": "nb-1", "query_vec": [0.1, 0.2], "top_k": 7},
            db.aql.last_bind_vars,
        )


if __name__ == "__main__":
    unittest.main()
