"""Offline GA release invariants; not substitutes for browser/OIDC/live tests."""
from importlib.util import spec_from_file_location, module_from_spec
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = spec_from_file_location('smoke', ROOT / 'scripts/ga_smoke.py')
smoke = module_from_spec(spec)
spec.loader.exec_module(smoke)


def read(path):
    return (ROOT / path).read_text()


class ReleaseTests(unittest.TestCase):
    def test_smoke_refuses_remote_http(self):
        with self.assertRaises(ValueError):
            smoke.validate_url('http://example.com')
        self.assertEqual(smoke.validate_url('http://localhost:8080/'), 'http://localhost:8080')
        self.assertEqual(smoke.validate_url('https://staging.example.com/'), 'https://staging.example.com')

    def test_smoke_rejects_url_credentials(self):
        for url in ('https://user:pass@example.org', 'https://example.org?token=x', 'ftp://example.org'):
            with self.subTest(url=url), self.assertRaises(ValueError):
                smoke.validate_url(url)

    def test_api_client_config_has_no_signing_secrets(self):
        source = read('api/Endpoints/ClientAuthEndpoints.cs')
        self.assertIn('/api/auth/client-config', source)
        self.assertIn('ClientId', source)
        for secret in ('Jwt:Key', 'PostgreSql', 'Password', 'clientSecret'):
            self.assertNotIn(secret, source)
        api = read('api/Program.cs')
        self.assertIn('app.MapClientAuthEndpoints()', api)
        self.assertIn('Authentication:ClientId', api)

    def test_authorization_code_pkce_and_callback_guard(self):
        auth = read('web/src/app/core/services/auth.service.ts')
        for word in ('code_challenge_method', 'S256', 'crypto.subtle.digest',
                     'code_verifier', 'sessionStorage.removeItem(TRANSACTION_KEY)',
                     "response_type', 'code'", "window.history.replaceState", 'expires_in'):
            self.assertIn(word, auth)
        self.assertNotIn("sessionStorage.setItem('incidentlens_access_token'", auth)
        self.assertIn("environment.production", auth)
        self.assertIn("target.origin !== base.origin", read('web/src/app/core/services/auth.interceptor.ts'))
        routes = read('web/src/app/app.routes.ts')
        self.assertIn("path: 'login'", routes)
        self.assertIn("path: 'auth/callback'", routes)
        self.assertIn('canActivate', routes)

    def test_runtime_configuration_is_wired_end_to_end(self):
        for name in ('compose.yaml', 'deploy/k8s/api.yaml', 'infra/terraform/kubernetes/main.tf'):
            with self.subTest(file=name):
                contents = read(name)
                self.assertIn('Authentication__ClientId', contents)
                self.assertIn('Authentication__Scope', contents)
        self.assertIn('OIDC_CLIENT_ID', read('scripts/preflight.py'))
        self.assertIn('OIDC_CLIENT_ID', read('deploy/.env.example'))

    def test_guides_and_release_notes_exist(self):
        for name in ('docs/AUTHENTICATION.md', 'docs/OPERATOR_GUIDE.md',
                     'docs/ADMIN_GUIDE.md', 'docs/RELEASE_ACCEPTANCE.md'):
            self.assertGreater(len(read(name)), 400)
        self.assertIn('## v1.0.0', read('CHANGELOG.md'))
        self.assertIn('source candidate', read('README.md'))


if __name__ == '__main__':
    unittest.main()
