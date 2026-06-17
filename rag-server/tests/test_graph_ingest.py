import unittest

from app import graph_ingest


class FakeAql:
    def __init__(self, chunk_rows):
        self.chunk_rows = chunk_rows
        self.last_query = None
        self.last_bind_vars = None

    def execute(self, query, bind_vars):
        self.last_query = query
        self.last_bind_vars = bind_vars
        return list(self.chunk_rows)


class FakeCollection:
    def __init__(self):
        self.insert_calls: list[dict] = []

    def insert_many(self, docs, overwrite_mode=None):
        self.insert_calls.append({"docs": docs, "overwrite_mode": overwrite_mode})


class FakeDb:
    def __init__(self, chunk_rows):
        self.aql = FakeAql(chunk_rows)
        self._collections: dict[str, FakeCollection] = {}

    def collection(self, name):
        return self._collections.setdefault(name, FakeCollection())


class NormalizeEntityNameTests(unittest.TestCase):
    def test_lowercases_and_strips_punctuation_and_whitespace(self):
        self.assertEqual("ada lovelace", graph_ingest.normalize_entity_name("  Ada, Lovelace! "))

    def test_nfkc_normalizes_fullwidth_characters(self):
        # Fullwidth "A" (U+FF21) should normalize to ASCII "a" after casefold.
        self.assertEqual("ada", graph_ingest.normalize_entity_name("Ａda"))


class ResolveAndAssembleTests(unittest.TestCase):
    def _chunk_extraction(self, chunk_index=0, mentions=None, facts=None):
        return {
            "chunk_index": chunk_index,
            "mentions": mentions or [],
            "facts": facts or [],
        }

    def test_writes_entity_for_each_distinct_mention(self):
        db = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        extraction = self._chunk_extraction(
            mentions=[
                {"entity_name": "Ada Lovelace", "entity_type": "person"},
                {"entity_name": "Charles Babbage", "entity_type": "person"},
            ]
        )

        result = graph_ingest.resolve_and_assemble(db, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        self.assertEqual(2, result["entities_written"])
        self.assertEqual([], result["skipped_chunks"])
        entities_written = db.collection("entities").insert_calls[0]["docs"]
        self.assertEqual(
            {"ada lovelace", "charles babbage"},
            {e["canonical_name"] for e in entities_written},
        )
        for doc in entities_written:
            self.assertEqual("nb-1", doc["notebook_id"])
            self.assertEqual("user-1", doc["user_id"])
            self.assertEqual("rv-1", doc["retrieval_version_id"])

    def test_merges_aliases_with_different_casing_into_one_entity(self):
        db = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        extraction = self._chunk_extraction(
            mentions=[
                {"entity_name": "Ada Lovelace", "entity_type": "person"},
                {"entity_name": "ADA LOVELACE", "entity_type": "person"},
            ]
        )

        result = graph_ingest.resolve_and_assemble(db, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        self.assertEqual(1, result["entities_written"])
        entity = db.collection("entities").insert_calls[0]["docs"][0]
        self.assertEqual(2, entity["mention_count"])
        self.assertIn("ADA LOVELACE", entity["aliases"])

    def test_writes_fact_with_participant_and_chunk_edges(self):
        db = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        extraction = self._chunk_extraction(
            facts=[
                {
                    "predicate": "wrote",
                    "statement_text": "Ada Lovelace wrote notes on the Analytical Engine.",
                    "confidence": 0.9,
                    "participants": [{"entity_name": "Ada Lovelace", "role": "subject"}],
                }
            ]
        )

        result = graph_ingest.resolve_and_assemble(db, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        self.assertEqual(1, result["facts_written"])
        self.assertEqual(1, result["entities_written"])
        # one fact_supported_by_chunk edge + one fact_has_participant edge
        self.assertEqual(2, result["edges_written"])

        fact_doc = db.collection("facts").insert_calls[0]["docs"][0]
        self.assertEqual("wrote", fact_doc["predicate"])

        participant_edge = db.collection("fact_has_participant").insert_calls[0]["docs"][0]
        self.assertEqual("subject", participant_edge["role"])
        self.assertTrue(participant_edge["_from"].startswith("facts/"))
        self.assertTrue(participant_edge["_to"].startswith("entities/"))

        chunk_edge = db.collection("fact_supported_by_chunk").insert_calls[0]["docs"][0]
        self.assertEqual("chunks/c0", chunk_edge["_to"])

    def test_skips_chunk_with_no_matching_chunk_id(self):
        db = FakeDb(chunk_rows=[])  # no chunks exist for this source/version
        extraction = self._chunk_extraction(
            chunk_index=5,
            mentions=[{"entity_name": "Orphaned Mention", "entity_type": "x"}],
        )

        result = graph_ingest.resolve_and_assemble(db, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        self.assertEqual([5], result["skipped_chunks"])
        self.assertEqual(0, result["entities_written"])

    def test_writes_use_overwrite_mode_replace_for_idempotent_reingest(self):
        db = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        extraction = self._chunk_extraction(mentions=[{"entity_name": "Ada", "entity_type": "person"}])

        graph_ingest.resolve_and_assemble(db, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        self.assertEqual("replace", db.collection("entities").insert_calls[0]["overwrite_mode"])

    def test_keys_are_deterministic_across_separate_calls(self):
        extraction = self._chunk_extraction(mentions=[{"entity_name": "Ada", "entity_type": "person"}])

        db1 = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        graph_ingest.resolve_and_assemble(db1, "src-1", "nb-1", "user-1", "rv-1", [extraction])
        db2 = FakeDb(chunk_rows=[{"chunk_index": 0, "_id": "chunks/c0"}])
        graph_ingest.resolve_and_assemble(db2, "src-1", "nb-1", "user-1", "rv-1", [extraction])

        key1 = db1.collection("entities").insert_calls[0]["docs"][0]["_key"]
        key2 = db2.collection("entities").insert_calls[0]["docs"][0]["_key"]
        self.assertEqual(key1, key2)


if __name__ == "__main__":
    unittest.main()
