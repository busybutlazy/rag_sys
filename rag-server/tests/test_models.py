import unittest

from pydantic import ValidationError

from app.models import IngestRequest


class ModelTests(unittest.TestCase):
    def test_ingest_request_requires_all_fields(self):
        with self.assertRaises(ValidationError):
            IngestRequest(source_id="s1", notebook_id="n1", file_path="/tmp/a.txt")


if __name__ == "__main__":
    unittest.main()
