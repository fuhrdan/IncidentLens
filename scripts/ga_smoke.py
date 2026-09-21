#!/usr/bin/env python3
"""Read-only staging checks. Refuses plain HTTP remote targets and redirects.

Environment: INCIDENTLENS_TOKEN_A, INCIDENTLENS_TOKEN_B, INCIDENTLENS_INCIDENT_A.
A and B must represent different tenants. Never put bearer tokens in argv/logs.
"""
import argparse
import ipaddress
import json
import os
from urllib.parse import urlparse, quote
from urllib.request import Request, build_opener, HTTPRedirectHandler
from urllib.error import HTTPError, URLError


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        raise ValueError('Unexpected redirect; refusing to forward credentials')


def validate_url(raw):
    url = urlparse(raw)
    if url.scheme not in ('http', 'https') or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise ValueError('Use a base URL with an explicit scheme and no credentials/query/fragment')
    if url.scheme == 'http' and url.hostname != 'localhost':
        try:
            if not ipaddress.ip_address(url.hostname).is_loopback:
                raise ValueError('Remote targets require HTTPS')
        except ValueError as ex:
            if str(ex) == 'Remote targets require HTTPS':
                raise
            raise ValueError('Remote targets require HTTPS') from ex
    return raw.rstrip('/')


def request(opener, base, path, token=None):
    headers = {'Accept': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = Request(base + path, headers=headers)
    try:
        response = opener.open(req, timeout=15)
        with response:
            payload = response.read(1_000_000)
            return response.status, payload
    except HTTPError as error:
        with error:
            return error.code, error.read(1_000_000)


def run(base, token_a, token_b, incident_id):
    opener = build_opener(NoRedirect())
    checks = [
        ('liveness', '/health/live', None, {200}),
        ('readiness', '/health/ready', None, {200}),
        ('public SPA config', '/api/auth/client-config', None, {200}),
        ('anonymous denied', '/api/incidents/', None, {401}),
        ('tenant A reads own queue', '/api/incidents/', token_a, {200}),
        ('tenant B reads own queue', '/api/incidents/', token_b, {200}),
        ('tenant A sees own incident', '/api/incidents/' + quote(incident_id, safe=''), token_a, {200}),
        ('tenant B cannot see tenant A incident', '/api/incidents/' + quote(incident_id, safe=''), token_b, {404}),
    ]
    failures = 0
    for name, path, token, expected in checks:
        status, body = request(opener, base, path, token)
        ok = status in expected
        if name == 'public SPA config' and ok:
            config = json.loads(body)
            ok = config.get('mode') == 'oidc' and bool(config.get('clientId'))
        print(('PASS' if ok else 'FAIL') + ' ' + name + ' status=' + str(status))
        failures += not ok
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', required=True)
    args = parser.parse_args()
    try:
        base = validate_url(args.base_url)
        token_a = os.environ['INCIDENTLENS_TOKEN_A']
        token_b = os.environ['INCIDENTLENS_TOKEN_B']
        incident_id = os.environ['INCIDENTLENS_INCIDENT_A']
        if not all((token_a, token_b, incident_id)) or token_a == token_b:
            raise ValueError('Require distinct tenant tokens A/B and one existing tenant-A incident')
        failures = run(base, token_a, token_b, incident_id)
    except (ValueError, KeyError, URLError, OSError, json.JSONDecodeError) as error:
        print('FAIL: ' + str(error))
        return 1
    print('Staging smoke:', 'PASS' if not failures else 'FAIL', '; full acceptance remains separate')
    return int(bool(failures))


if __name__ == '__main__':
    raise SystemExit(main())
