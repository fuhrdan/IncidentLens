#!/usr/bin/env python3
"""IncidentLens v0.7.0 PostgreSQL backup, verification, restore, and drill.

Requires PostgreSQL client programs in PATH and PG* connection environment
variables. Never accepts a password on the command line. All commands use an
argument array (no shell). Backups contain sensitive data: secure the directory
and encrypt backups at rest using your storage or secret-management provider.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid


def run(args: list[str], capture: bool = False) -> str:
    try:
        result = subprocess.run(args, check=True, text=True, capture_output=capture)
    except FileNotFoundError as exc:
        raise RuntimeError(f"Required PostgreSQL program is not installed: {args[0]}") from exc
    except subprocess.CalledProcessError as exc:
        raise RuntimeError(f"PostgreSQL operation failed: {args[0]} (exit {exc.returncode})") from exc
    return result.stdout.strip() if capture else ""


def source_db() -> str:
    name = os.environ.get("PGDATABASE", "")
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", name):
        raise RuntimeError("Set PGDATABASE to the explicit source database name (no connection URLs).")
    return name


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def backup(directory: Path) -> Path:
    database = source_db()
    directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    if os.name != "nt":
        if directory.stat().st_mode & 0o077:
            raise RuntimeError("Backup directory is readable by other users; chmod 700 it first.")
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    name = f"incidentlens-{stamp}-{uuid.uuid4().hex[:8]}"
    dump = directory / f"{name}.dump"
    manifest_path = directory / f"{name}.json"
    fd, temporary_name = tempfile.mkstemp(prefix=".incidentlens-", dir=directory)
    os.close(fd)
    temporary = Path(temporary_name)
    try:
        run(["pg_dump", "--format=custom", "--no-owner", "--no-acl",
             "--dbname", database, "--file", str(temporary)])
        # Detect a corrupt or incomplete archive before publishing it.
        run(["pg_restore", "--list", str(temporary)], capture=True)
        checksum = digest(temporary)
        manifest = {
            "format": "IncidentLens PostgreSQL custom dump v1",
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "database": database,
            "archive": dump.name,
            "sha256": checksum,
            "sizeBytes": temporary.stat().st_size,
            "pgClientVersion": run(["pg_dump", "--version"], capture=True),
            "encrypted": False,
        }
        if manifest["sizeBytes"] == 0:
            raise RuntimeError("Empty database backup refused.")
        temporary.replace(dump)
        # Create the manifest with restrictive permissions from the very first byte.
        manifest_fd = os.open(manifest_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(manifest_fd, "w", encoding="utf-8") as output:
            output.write(json.dumps(manifest, indent=2) + "\n")
        if os.name != "nt":
            dump.chmod(0o600)
        print(f"Backup created: {dump}\nManifest: {manifest_path}\nSHA256: {checksum}")
        print("WARNING: backup is NOT encrypted; encrypt at rest and move to restricted off-host storage.")
        return manifest_path
    finally:
        temporary.unlink(missing_ok=True)


def verify(manifest_path: Path) -> tuple[dict, Path]:
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("format") != "IncidentLens PostgreSQL custom dump v1":
        raise RuntimeError("Unrecognized backup manifest format.")
    filename = manifest.get("archive", "")
    if not isinstance(filename, str) or not filename.endswith(".dump") or Path(filename).name != filename:
        raise RuntimeError("Unsafe backup archive path.")
    archive = manifest_path.parent / filename
    if not archive.is_file() or archive.stat().st_size != manifest.get("sizeBytes"):
        raise RuntimeError("Backup missing or size mismatch.")
    if digest(archive) != manifest.get("sha256"):
        raise RuntimeError("Backup SHA256 mismatch: do not restore this archive.")
    run(["pg_restore", "--list", str(archive)], capture=True)
    print(f"Verified archive integrity and pg_restore catalog: {archive}")
    return manifest, archive


def target_is_empty(database: str) -> bool:
    sql = ("SELECT count(*) FROM information_schema.tables "
           "WHERE table_schema NOT IN ('pg_catalog','information_schema')")
    value = run(["psql", "--no-psqlrc", "--tuples-only", "--no-align", "--set", "ON_ERROR_STOP=1",
                 "--dbname", database, "--command", sql], capture=True)
    return value == "0"


def restore(manifest_path: Path, database: str, confirmation: str) -> None:
    source = source_db()
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", database):
        raise RuntimeError("Target database name must be a simple identifier.")
    if database == source:
        raise RuntimeError("Refusing to restore into the configured source database.")
    if confirmation != f"RESTORE:{database}":
        raise RuntimeError(f"Explicit confirmation required: --confirm RESTORE:{database}")
    manifest, archive = verify(manifest_path)
    if not target_is_empty(database):
        raise RuntimeError("Target must exist and have no user tables. Existing databases are never overwritten.")
    print(f"Restoring {manifest['database']} to empty target {database}.")
    run(["pg_restore", "--exit-on-error", "--single-transaction", "--no-owner",
         "--no-privileges", "--dbname", database, str(archive)])
    print(f"Restore completed: {database}. Run application and data validation before promotion.")


def row_counts(database: str) -> dict[str, int]:
    """Read core evidence table counts from source and restored database."""
    result: dict[str, int] = {}
    for table in ("Incidents", "AuditRecords", "Postmortems", "TimelineEvents",
                  "ServiceHealthSnapshots", "OutboxMessages", "__EFMigrationsHistory"):
        sql = f'SELECT count(*) FROM public."{table}"'
        value = run(["psql", "--no-psqlrc", "--tuples-only", "--no-align",
                     "--set", "ON_ERROR_STOP=1", "--dbname", database,
                     "--command", sql], capture=True)
        result[table] = int(value)
    return result


def drill(directory: Path) -> None:
    """Create a disposable destination, compare critical row counts, drop on success."""
    source = source_db()
    target = "incidentlens_drill_" + uuid.uuid4().hex[:12]
    manifest_path = backup(directory)
    verify(manifest_path)
    baseline = row_counts(source)
    run(["createdb", target])
    try:
        restore(manifest_path, target, f"RESTORE:{target}")
        recovered = row_counts(target)
        if baseline != recovered:
            raise RuntimeError(f"Recovery drill FAILED: row counts differ: {baseline} vs {recovered}")
        print(f"Recovery drill PASSED: verified row counts: {recovered}")
    except BaseException:
        print(f"Drill failed. Preserved disposable target '{target}' for investigation.", file=sys.stderr)
        raise
    run(["dropdb", target])
    print("Disposable drill database removed after successful verification.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("backup").add_argument("--output-dir", required=True, type=Path)
    sub.add_parser("verify").add_argument("--manifest", required=True, type=Path)
    restore_parser = sub.add_parser("restore")
    restore_parser.add_argument("--manifest", required=True, type=Path)
    restore_parser.add_argument("--target-database", required=True)
    restore_parser.add_argument("--confirm", required=True)
    sub.add_parser("drill").add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "backup":
            backup(args.output_dir)
        elif args.command == "verify":
            verify(args.manifest)
        elif args.command == "restore":
            restore(args.manifest, args.target_database, args.confirm)
        else:
            drill(args.output_dir)
    except (RuntimeError, ValueError, OSError, json.JSONDecodeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
