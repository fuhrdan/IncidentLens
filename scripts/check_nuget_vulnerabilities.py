#!/usr/bin/env python3
"""Fail CI if NuGet JSON audit reports any advisory for a direct/transitive package."""
import json
import sys
from pathlib import Path


def findings(report):
    result = []
    for project in report.get("projects", []):
        for framework in project.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(kind, []):
                    for advisory in package.get("vulnerabilities", []):
                        result.append((package.get("id", "unknown"), advisory.get("severity", "unknown"),
                                       advisory.get("advisoryurl", advisory.get("advisoryUrl", ""))))
    return result


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 1:
        print("Usage: check_nuget_vulnerabilities.py REPORT.json", file=sys.stderr)
        return 2
    try:
        report = json.loads(Path(argv[0]).read_text(encoding="utf-8"))
        if (not isinstance(report, dict) or not isinstance(report.get("projects"), list)
                or not report["projects"]):
            raise ValueError("NuGet report contains no project audit results.")
        issues = findings(report)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"Invalid/unavailable NuGet report: {error}", file=sys.stderr)
        return 2
    for name, severity, advisory in issues:
        print(f"NuGet advisory: {name} ({severity}) {advisory}")
    if issues:
        print(f"NuGet audit failed: {len(issues)} advisories reported.", file=sys.stderr)
        return 1
    print("NuGet audit: no advisories reported in direct/transitive packages.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
