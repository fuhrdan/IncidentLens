"""Offline safety tests; a real PostgreSQL restore drill is still required."""
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import recovery


class RecoveryTests(unittest.TestCase):
    def test_missing_source_fails_closed(self):
        with patch.dict(os.environ, {"PGDATABASE": ""}):
            with self.assertRaisesRegex(RuntimeError, "PGDATABASE"):
                recovery.source_db()

    def test_restore_refuses_same_database_before_touching_archive(self):
        with patch.dict(os.environ, {"PGDATABASE": "incidentlens"}):
            with self.assertRaisesRegex(RuntimeError, "source database"):
                recovery.restore(Path("missing.json"), "incidentlens", "RESTORE:incidentlens")

    def test_restore_needs_exact_explicit_confirmation(self):
        with patch.dict(os.environ, {"PGDATABASE": "incidentlens"}):
            with self.assertRaisesRegex(RuntimeError, "Explicit confirmation"):
                recovery.restore(Path("missing.json"), "recover_db", "yes")

    def test_verify_refuses_modified_archive(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            archive = directory / "sample.dump"
            archive.write_bytes(b"badly changed archive")
            manifest = directory / "sample.json"
            manifest.write_text(json.dumps({"format": "IncidentLens PostgreSQL custom dump v1",
                "archive": archive.name, "sizeBytes": archive.stat().st_size,
                "sha256": "0" * 64}), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "SHA256 mismatch"):
                recovery.verify(manifest)

    def test_verify_refuses_path_traversal(self):
        with tempfile.TemporaryDirectory() as tmp:
            manifest = Path(tmp) / "sample.json"
            manifest.write_text(json.dumps({"format": "IncidentLens PostgreSQL custom dump v1",
                "archive": "../secret.dump", "sizeBytes": 1,
                "sha256": "0" * 64}), encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "Unsafe backup archive path"):
                recovery.verify(manifest)


    def test_backup_publishes_restricted_files_and_verifiable_hash(self):
        with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, {"PGDATABASE": "incidentlens"}):
            directory = Path(tmp) / "protected"
            def fake_run(arguments, capture=False):
                if arguments[0] == "pg_dump" and "--file" in arguments:
                    Path(arguments[arguments.index("--file") + 1]).write_bytes(b"test-archive-bytes")
                return "pg_dump (PostgreSQL) 16" if arguments[:2] == ["pg_dump", "--version"] else "CATALOG"
            with patch.object(recovery, "run", side_effect=fake_run):
                with contextlib.redirect_stdout(io.StringIO()):
                    manifest_path = recovery.backup(directory)
                manifest, archive = recovery.verify(manifest_path)
                self.assertEqual(manifest["sha256"], recovery.digest(archive))
                self.assertEqual(manifest["sizeBytes"], len(b"test-archive-bytes"))
                if os.name != "nt":
                    self.assertEqual(manifest_path.stat().st_mode & 0o777, 0o600)
                    self.assertEqual(archive.stat().st_mode & 0o777, 0o600)

    def test_restore_only_targets_validated_empty_database(self):
        with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, {"PGDATABASE": "incidentlens"}):
            directory = Path(tmp)
            archive = directory / "backup.dump"
            archive.write_bytes(b"archive-data")
            manifest = directory / "backup.json"
            manifest.write_text(json.dumps({"format": "IncidentLens PostgreSQL custom dump v1",
                "archive": archive.name, "sizeBytes": archive.stat().st_size,
                "sha256": recovery.digest(archive), "database": "incidentlens"}), encoding="utf-8")
            observed = []
            with patch.object(recovery, "run", side_effect=lambda args, capture=False: observed.append(args) or "CATALOG"), \
                    patch.object(recovery, "target_is_empty", return_value=True):
                with contextlib.redirect_stdout(io.StringIO()):
                    recovery.restore(manifest, "recovery_db", "RESTORE:recovery_db")
            self.assertEqual(observed[-1][0], "pg_restore")
            self.assertIn("--single-transaction", observed[-1])
            self.assertIn("--exit-on-error", observed[-1])
            self.assertEqual(observed[-1][observed[-1].index("--dbname") + 1], "recovery_db")

    def test_restore_refuses_existing_tables(self):
        with tempfile.TemporaryDirectory() as tmp, patch.dict(os.environ, {"PGDATABASE": "incidentlens"}):
            directory = Path(tmp)
            archive = directory / "backup.dump"
            archive.write_bytes(b"archive-data")
            manifest = directory / "backup.json"
            manifest.write_text(json.dumps({"format": "IncidentLens PostgreSQL custom dump v1",
                "archive": archive.name, "sizeBytes": archive.stat().st_size,
                "sha256": recovery.digest(archive), "database": "incidentlens"}), encoding="utf-8")
            with patch.object(recovery, "run", return_value="CATALOG"), \
                    patch.object(recovery, "target_is_empty", return_value=False):
                with self.assertRaisesRegex(RuntimeError, "no user tables"):
                    recovery.restore(manifest, "recovery_db", "RESTORE:recovery_db")


if __name__ == "__main__":
    unittest.main()
