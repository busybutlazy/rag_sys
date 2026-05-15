import unittest

from app import chunker


class ChunkerTests(unittest.TestCase):
    def test_chunk_text_uses_overlap(self):
        chunks = chunker.chunk_text("abcdefghij", chunk_size=6, chunk_overlap=2)

        self.assertEqual(["abcdef", "efghij"], chunks)

    def test_chunk_text_returns_empty_for_blank_text(self):
        self.assertEqual([], chunker.chunk_text("   "))


if __name__ == "__main__":
    unittest.main()
