"""Offline deployment safeguards; not substitutes for live container tests."""
from pathlib import Path
import unittest
import yaml
ROOT = Path(__file__).resolve().parents[2]

def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

class DeploymentReferenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compose = yaml.safe_load(read("compose.yaml"))
        cls.k8s = {}
        for path in (ROOT / "deploy/k8s").glob("*.yaml"):
            for item in yaml.safe_load_all(path.read_text()):
                if item:
                    cls.k8s[item["kind"], item["metadata"]["name"]] = item

    def test_full_compose_stack_private_services(self):
        svc = self.compose["services"]
        self.assertEqual(set(svc), {"postgres", "api", "web", "edge"})
        for name in ("postgres", "api", "web"):
            self.assertNotIn("ports", svc[name])
            self.assertEqual(svc[name]["networks"], ["internal"])
        self.assertEqual(set(svc["edge"]["ports"]), {"80:80", "443:443"})

    def test_compose_requires_oidc_and_db_secret(self):
        env = self.compose["services"]["api"]["environment"]
        self.assertEqual(env["ASPNETCORE_ENVIRONMENT"], "Production")
        self.assertIn("${OIDC_AUTHORITY:?", env["Authentication__Authority"])
        self.assertIn("${OIDC_AUDIENCE:?", env["Authentication__Audience"])
        self.assertIn("${OIDC_CLIENT_ID:?", env["Authentication__ClientId"])
        self.assertIn("${POSTGRES_PASSWORD:?", env["ConnectionStrings__PostgreSql"])
        self.assertEqual(env["Retention__AllowPurge"], "false")
        self.assertIn("/health/ready", " ".join(self.compose["services"]["api"]["healthcheck"]["test"]))

    def test_nginx_https_and_proxy_routes(self):
        edge, web = read("deploy/nginx/edge.conf"), read("deploy/nginx/web.conf")
        for token in ("listen 443 ssl", "return 308 https://", "proxy_pass http://web:8080"):
            self.assertIn(token, edge)
        for token in ("proxy_pass http://api:8080", "location ^~ /hubs/", "location ^~ /api/"):
            self.assertIn(token, web)

    def test_docker_images_are_production(self):
        api, web = read("api/Dockerfile"), read("web/Dockerfile")
        self.assertIn("rm -f /out/appsettings.Development.json", api)
        self.assertIn("USER $APP_UID", api)
        self.assertIn("npm ci", web)
        self.assertIn("--configuration production", web)
        self.assertIn("dist/web/browser/", web)
        self.assertIn("deploy/certs/", read(".gitignore"))
        self.assertIn("*.pem", read(".dockerignore"))

    def test_api_health_separated(self):
        source = read("api/Program.cs")
        for token in ('app.MapHealthChecks("/health/live"', 'app.MapHealthChecks("/health/ready"', 'tags: ["ready"]', 'serviceVersion: "1.0.0"'):
            self.assertIn(token, source)

    def test_k8s_single_replica_probes_secret(self):
        api = self.k8s["Deployment", "api"]["spec"]
        self.assertEqual(api["replicas"], 1)
        self.assertEqual(api["strategy"]["type"], "Recreate")
        container = api["template"]["spec"]["containers"][0]
        self.assertEqual(container["startupProbe"]["httpGet"]["path"], "/health/live")
        self.assertEqual(container["readinessProbe"]["httpGet"]["path"], "/health/ready")
        self.assertEqual(container["livenessProbe"]["httpGet"]["path"], "/health/live")
        secret = [e for e in container["env"] if e["name"] == "ConnectionStrings__PostgreSql"][0]
        self.assertEqual(secret["valueFrom"]["secretKeyRef"]["name"], "incidentlens-secrets")

    def test_k8s_tls_and_internal_services(self):
        ingress = self.k8s["Ingress", "incidentlens"]["spec"]
        self.assertEqual(ingress["tls"][0]["secretName"], "incidentlens-tls")
        self.assertEqual(ingress["rules"][0]["http"]["paths"][0]["backend"]["service"]["name"], "web")
        for name in ("api", "web"):
            self.assertEqual(self.k8s["Service", name]["spec"].get("type", "ClusterIP"), "ClusterIP")

    def test_terraform_references_external_secret(self):
        tf = read("infra/terraform/kubernetes/main.tf")
        self.assertNotIn('resource "kubernetes_secret', tf)
        for token in ('secret_key_ref', 'type = "Recreate"', 'kubernetes_ingress_v1'):
            self.assertIn(token, tf)

    def test_runbook_and_changelog(self):
        guide = read("docs/DEPLOYMENT.md").lower()
        for term in ("demo", "rpo", "rto", "recreate", "oidc", "rollback", "backup", "tls", "terraform"):
            self.assertIn(term, guide)
        self.assertIn("0.8.0", read("CHANGELOG.md"))

if __name__ == "__main__":
    unittest.main()
