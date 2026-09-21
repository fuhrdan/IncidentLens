"""No live service or tokens needed for the bounded load-harness tests."""
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import contextlib
import io
import os
import threading
import unittest

file = Path(__file__).resolve().parents[2] / "scripts/load_test.py"
spec = spec_from_file_location("incidentlens_load", file)
load = module_from_spec(spec)
spec.loader.exec_module(load)


class LoadHarnessTests(unittest.TestCase):
    def test_nearest_rank_percentiles(self):
        self.assertEqual(load.percentile([1, 2, 3, 4, 5], 95), 5)
        self.assertEqual(load.percentile([5], 99), 5)
        self.assertIsNone(load.percentile([], 95))

    def test_remote_requires_opt_in_and_https(self):
        with self.assertRaises(ValueError):
            load.validate_target("https://remote.example.org", False)
        with self.assertRaises(ValueError):
            load.validate_target("http://remote.example.org", True)
        self.assertEqual(load.validate_target("https://remote.example.org", True),
                         "https://remote.example.org/api/incidents")

    def test_rejects_credentials_paths_and_missing_tokens(self):
        for url in ("http://user:pass@localhost", "http://localhost/api/alerts", "file:///etc/passwd"):
            with self.assertRaises(ValueError):
                load.validate_target(url, False)
        with self.assertRaises(ValueError):
            load.benchmark("http://localhost/api/incidents", " ", 1, 1, 1)

    def test_does_not_send_tokens_in_output_and_measures_responses(self):
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                if self.path != "/api/incidents" or self.headers.get("Authorization") != "Bearer test-secret":
                    self.send_response(403)
                else:
                    self.send_response(200)
                self.end_headers()
                self.wfile.write(b"{}")

            def log_message(self, *args):
                pass

        with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                url = f"http://127.0.0.1:{server.server_port}/api/incidents"
                result = load.benchmark(url, "test-secret", 10, 2, 2)
                self.assertEqual(result["statuses"], {200: 10})
                self.assertEqual(result["error_percent"], 0)
                self.assertGreaterEqual(result["p95_ms"], 0)
                self.assertNotIn("test-secret", str(result))
            finally:
                server.shutdown()
                thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
