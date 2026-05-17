import asyncio
import os
import unittest

os.environ.setdefault("OPENAI_API_KEY", "test")

from app.agent import _run_tool


class AgentToolIsolationTests(unittest.TestCase):
    def test_create_note_rejects_notebook_override(self):
        with self.assertRaisesRegex(ValueError, "active notebook"):
            asyncio.run(
                _run_tool(
                    "create_note",
                    {"notebook_id": "other", "content": "secret"},
                    "active",
                    "user-1",
                    "Bearer token",
                )
            )


if __name__ == "__main__":
    unittest.main()
