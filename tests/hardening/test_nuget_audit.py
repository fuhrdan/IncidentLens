import importlib.util
from pathlib import Path
import json
import tempfile
import unittest

path = Path(__file__).resolve().parents[2] / "scripts/check_nuget_vulnerabilities.py"
spec = importlib.util.spec_from_file_location("nuget_audit", path)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class NuGetAuditTests(unittest.TestCase):
    def test_detects_transitive_and_direct_advisories(self):
        report = {"projects": [{"frameworks": [{
            "topLevelPackages": [{"id": "Direct", "vulnerabilities": [{"severity": "High"}]}],
            "transitivePackages": [{"id": "Transitive", "vulnerabilities": [{"severity": "Critical"}]}],
        }]}]}
        self.assertEqual(["Direct", "Transitive"], [row[0] for row in audit.findings(report)])

    def test_missing_report_fails_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "report.json"
            self.assertEqual(2, audit.main([str(path)]))
            path.write_text(json.dumps({"projects": []}))
            self.assertEqual(2, audit.main([str(path)]))
            path.write_text(json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
                {"id": "Clean", "vulnerabilities": []}]}]}]}))
            self.assertEqual(0, audit.main([str(path)]))
            path.write_text(json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
                {"id": "Pkg", "vulnerabilities": [{"severity": "High"}]}]}]}]}))
            self.assertEqual(1, audit.main([str(path)]))


if __name__ == "__main__":
    unittest.main()
