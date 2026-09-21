#!/usr/bin/env python3
"""Fail early on common, unsafe Compose deployment configuration mistakes.

Uses only Python's standard library and never prints credential values.
Does not replace a certificate trust-chain check or an identity-provider test.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import ssl
from urllib.parse import urlparse


def config_from_file(path: Path) -> dict[str, str]:
    if not path.is_file():
        raise ValueError(f"Missing {path}; copy deploy/.env.example and configure it")
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise ValueError("Invalid .env assignment")
        name, value = line.split("=", 1)
        values[name.strip()] = value.strip().strip('"').strip("'")
    return values


def validate_config(values: dict[str, str]) -> None:
    password = values.get("POSTGRES_PASSWORD", "")
    if len(password) < 20 or "REPLACE" in password.upper() or ";" in password:
        raise ValueError("Set a unique PostgreSQL password of at least 20 characters, without semicolons")
    authority = values.get("OIDC_AUTHORITY", "")
    address = urlparse(authority)
    if address.scheme != "https" or not address.hostname or address.hostname.endswith("example.com"):
        raise ValueError("Set OIDC_AUTHORITY to a real HTTPS issuer, not the example")
    audience = values.get("OIDC_AUDIENCE", "")
    if not audience or "REPLACE" in audience.upper():
        raise ValueError("Set OIDC_AUDIENCE to the configured API audience")
    client_id = values.get("OIDC_CLIENT_ID", "")
    if not client_id or "REPLACE" in client_id.upper():
        raise ValueError("Set OIDC_CLIENT_ID to your public SPA registration")
    if "offline_access" in values.get("OIDC_SCOPE", "").split():
        raise ValueError("Do not request persistent refresh-token/offline access in this SPA")


def validate_tls(directory: Path) -> None:
    cert, key = directory / "fullchain.pem", directory / "privkey.pem"
    if not cert.is_file() or not key.is_file():
        raise ValueError("Install fullchain.pem and privkey.pem in deploy/certs")
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    try:
        context.load_cert_chain(str(cert), str(key))
    except (ssl.SSLError, OSError) as error:
        raise ValueError("TLS certificate/private key is missing, invalid, or mismatched") from error
    # Certificate issuer trust, hostname, and expiry require live testing.


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, default=Path(".env"))
    parser.add_argument("--cert-dir", type=Path, default=Path("deploy/certs"))
    args = parser.parse_args()
    try:
        validate_config(config_from_file(args.env_file))
        validate_tls(args.cert_dir)
    except ValueError as error:
        print(f"Preflight FAILED: {error}")
        return 1
    print("Preflight passed: required variables and TLS key pair present; live trust/issuer check still required")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
