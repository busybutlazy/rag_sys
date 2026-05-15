import os
import subprocess
import sys
import unittest


class InternalSecretTests(unittest.TestCase):
    def test_rag_server_rejects_short_secret_on_boot(self):
        env = os.environ.copy()
        env["RAG_INTERNAL_SECRET"] = "short"
        env["INTERNAL_SECRET"] = ""

        result = subprocess.run(
            [sys.executable, "-c", "import app.main"],
            cwd=".",
            env=env,
            capture_output=True,
            text=True,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("RAG_INTERNAL_SECRET must be at least", result.stderr + result.stdout)


if __name__ == "__main__":
    unittest.main()
