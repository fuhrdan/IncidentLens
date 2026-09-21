#!/usr/bin/env python3
"""Bounded, read-only live API benchmark. Never mutates incidents.

Run only against an environment you administer. Remote targets require the
explicit --allow-remote switch. Authorization tokens are NOT printed or logged.
"""
import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
import json
import math
import os
import statistics
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import HTTPRedirectHandler, Request, build_opener


def percentile(values, number):
    """Nearest-rank percentile, defined even for a one-request smoke run."""
    if not values:
        return None
    return sorted(values)[max(0, math.ceil(len(values) * number / 100) - 1)]


def validate_target(url, allow_remote):
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("Provide a complete http(s) URL with no embedded credentials.")
    if parsed.hostname not in ("localhost", "127.0.0.1", "::1") and not allow_remote:
        raise ValueError("Remote load testing requires --allow-remote and target-owner permission.")
    if parsed.hostname not in ("localhost", "127.0.0.1", "::1") and parsed.scheme != "https":
        raise ValueError("Remote token-bearing load tests require HTTPS.")
    if parsed.path.rstrip("/") not in ("", "/api/incidents") or parsed.query or parsed.fragment:
        raise ValueError("Supply the service origin or /api/incidents only.")
    return parsed.geturl().rstrip("/") if parsed.path.rstrip("/") == "/api/incidents" else parsed.geturl().rstrip("/") + "/api/incidents"


class NoRedirect(HTTPRedirectHandler):
    """Never forward an operator's bearer token to a redirect target."""
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def make_request(url, token, timeout):
    started = time.perf_counter()
    request = Request(url, headers={"Authorization": f"Bearer {token}", "Accept": "application/json"})
    try:
        with build_opener(NoRedirect()).open(request, timeout=timeout) as response:
            return response.status, (time.perf_counter() - started) * 1000
    except HTTPError as error:
        return error.code, (time.perf_counter() - started) * 1000
    except (OSError, URLError, TimeoutError):
        return 0, (time.perf_counter() - started) * 1000


def benchmark(url, token, requests, concurrency, timeout):
    if not token.strip():
        raise ValueError("INCIDENTLENS_TOKEN must contain a valid bearer token.")
    started = time.perf_counter()
    statuses = Counter()
    durations = []
    with ThreadPoolExecutor(max_workers=concurrency) as executor:
        futures = [executor.submit(make_request, url, token, timeout) for _ in range(requests)]
        for future in as_completed(futures):
            status, elapsed_ms = future.result()
            statuses[status] += 1
            durations.append(elapsed_ms)
    elapsed = time.perf_counter() - started
    return {
        "requests": requests,
        "concurrency": concurrency,
        "seconds": round(elapsed, 3),
        "throughput_rps": round(requests / max(elapsed, 0.001), 2),
        "statuses": dict(sorted(statuses.items())),
        "p50_ms": round(percentile(durations, 50), 2),
        "p95_ms": round(percentile(durations, 95), 2),
        "p99_ms": round(percentile(durations, 99), 2),
        "mean_ms": round(statistics.mean(durations), 2),
        "error_percent": round(100 * (requests - statuses[200]) / requests, 2),
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--allow-remote", action="store_true")
    parser.add_argument("--requests", type=int, default=100)
    parser.add_argument("--concurrency", type=int, default=5)
    parser.add_argument("--timeout", type=float, default=10)
    parser.add_argument("--max-p95-ms", type=float, default=1000)
    parser.add_argument("--max-error-percent", type=float, default=1)
    parser.add_argument("--output", help="Write JSON results to a local file (no secrets).")
    args = parser.parse_args(argv)
    try:
        if not 1 <= args.requests <= 10000 or not 1 <= args.concurrency <= 100:
            raise ValueError("Requests must be 1..10000 and concurrency 1..100.")
        if not 0 < args.timeout <= 120 or args.max_p95_ms <= 0 or not 0 <= args.max_error_percent <= 100:
            raise ValueError("Invalid timeout or acceptance threshold.")
        url = validate_target(args.base_url, args.allow_remote)
        result = benchmark(url, os.environ.get("INCIDENTLENS_TOKEN", ""),
                           args.requests, args.concurrency, args.timeout)
        result["pass"] = result["p95_ms"] <= args.max_p95_ms and result["error_percent"] <= args.max_error_percent
        printable = json.dumps(result, indent=2)
        print(printable)
        if args.output:
            from pathlib import Path
            Path(args.output).write_text(printable + "\n", encoding="utf-8")
        return 0 if result["pass"] else 1
    except ValueError as exception:
        print(f"Load-test configuration: {exception}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
