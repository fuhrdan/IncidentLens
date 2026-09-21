"""Tests for Compose preflight safety gates, without using real credentials."""
from importlib.util import spec_from_file_location, module_from_spec
from pathlib import Path
import tempfile
import unittest

FILE = Path(__file__).resolve().parents[2] / "scripts/preflight.py"
spec = spec_from_file_location("preflight", FILE)
preflight = module_from_spec(spec)
spec.loader.exec_module(preflight)

class PreflightTests(unittest.TestCase):
    def test_rejects_sample_password(self):
        with self.assertRaises(ValueError):
            preflight.validate_config({"POSTGRES_PASSWORD": "REPLACE_WITH_LONG_RANDOM_DATABASE_PASSWORD", "OIDC_AUTHORITY": "https://id.real.org", "OIDC_AUDIENCE": "api"})

    def test_rejects_example_issuer(self):
        with self.assertRaises(ValueError):
            preflight.validate_config({"POSTGRES_PASSWORD": "unpredictablelongpassword123", "OIDC_AUTHORITY": "https://id.example.com", "OIDC_AUDIENCE": "api"})

    def test_accepts_valid_shape_without_exposing_secret(self):
        preflight.validate_config({"POSTGRES_PASSWORD": "unpredictablelongpassword123", "OIDC_AUTHORITY": "https://id.real.org/realm", "OIDC_AUDIENCE": "incidentlens-api", "OIDC_CLIENT_ID": "incidentlens-spa"})

    def test_missing_spa_client_id_is_rejected(self):
        with self.assertRaises(ValueError):
            preflight.validate_config({"POSTGRES_PASSWORD": "unpredictablelongpassword123",
                                       "OIDC_AUTHORITY": "https://id.real.org/realm",
                                       "OIDC_AUDIENCE": "incidentlens-api"})

    def test_offline_access_scope_is_rejected(self):
        with self.assertRaises(ValueError):
            preflight.validate_config({"POSTGRES_PASSWORD": "unpredictablelongpassword123",
                                       "OIDC_AUTHORITY": "https://id.real.org/realm",
                                       "OIDC_AUDIENCE": "incidentlens-api",
                                       "OIDC_CLIENT_ID": "incidentlens-spa",
                                       "OIDC_SCOPE": "openid profile offline_access"})

    def test_missing_certificate_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                preflight.validate_tls(Path(directory))

    def test_config_file_parses_without_echoing_values(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            path.write_text('# comment\nPOSTGRES_PASSWORD="unpredictablelongpassword123"\n')
            self.assertEqual(preflight.config_from_file(path)["POSTGRES_PASSWORD"], "unpredictablelongpassword123")

if __name__ == "__main__":
    unittest.main()

