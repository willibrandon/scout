"""Run the CodeQL findings gate against SARIF documents through its actual CLI."""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class VerifyCodeQlResultsTests(unittest.TestCase):
    def test_clean_scan_passes(self) -> None:
        result = self.run_gate({"results": [], "invocations": [{"executionSuccessful": True}]})
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("zero findings", result.stdout)

    def test_every_severity_and_suppressed_or_existing_finding_fails(self) -> None:
        for level in ("error", "warning", "note", "none"):
            for extra in ({}, {"baselineState": "unchanged"}, {"suppressions": [{"kind": "inSource"}]}):
                with self.subTest(level=level, extra=extra):
                    result = self.run_gate({"results": [{
                        "ruleId": "test/security", "level": level,
                        "message": {"text": "Security finding"}, **extra,
                    }]})
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("test/security", result.stderr)

    def test_missing_results_or_invalid_finding_fails(self) -> None:
        for run in ({}, {"results": None}, {"results": {}}, {"results": [None]}):
            with self.subTest(run=run):
                self.assertNotEqual(0, self.run_gate(run).returncode)

    def test_large_findings_remain_failures_with_bounded_located_logs(self) -> None:
        result = self.run_gate({"results": [{
            "ruleId": "test/security",
            "message": {"text": "Huge message\n" + "detail " * 100_000},
            "locations": [{"physicalLocation": {
                "artifactLocation": {"uri": "src/Example.cs"},
                "region": {"startLine": 42},
            }}],
        }]})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("src/Example.cs:42: test/security: Huge message", result.stderr)
        self.assertIn("full message in SARIF artifact", result.stderr)
        self.assertIn("1 finding(s)", result.stderr)
        self.assertLess(len(result.stderr), 1_000)

    def test_failed_analysis_or_error_notification_fails(self) -> None:
        for invocation in (
            {"executionSuccessful": False},
            {"toolExecutionNotifications": [{"level": "error", "message": {"text": "Extraction failed"}}]},
            {"toolConfigurationNotifications": [{"level": "error", "message": {"text": "Invalid query"}}]},
        ):
            with self.subTest(invocation=invocation):
                self.assertNotEqual(0, self.run_gate({"results": [], "invocations": [invocation]}).returncode)

    def test_empty_missing_or_malformed_document_fails(self) -> None:
        for contents in (None, "{", "{}", '{"version":"2.1.0","runs":[]}'):
            with self.subTest(contents=contents):
                result = self.invoke({} if contents is None else {"scan.sarif": contents})
                self.assertNotEqual(0, result.returncode)
                self.assertIn("verification failed", result.stderr)

    def test_findings_in_any_file_or_run_fail(self) -> None:
        finding = {"ruleId": "test/security", "message": {"text": "Finding in second run"}}
        result = self.invoke({
            "clean.sarif": json.dumps({"version": "2.1.0", "runs": [{"results": []}]}),
            "nested/findings.sarif": json.dumps({"version": "2.1.0", "runs": [
                {"results": []}, {"results": [finding]},
            ]}),
        })
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Finding in second run", result.stderr)

    def run_gate(self, run: dict) -> subprocess.CompletedProcess[str]:
        return self.invoke({"scan.sarif": json.dumps({"version": "2.1.0", "runs": [run]})})

    def invoke(self, documents: dict[str, str]) -> subprocess.CompletedProcess[str]:
        script = Path(__file__).resolve().parents[2] / "eng" / "verify-codeql-results.py"
        with tempfile.TemporaryDirectory() as directory:
            for relative_path, contents in documents.items():
                path = Path(directory) / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(contents, encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(script), directory], capture_output=True, text=True, check=False,
            )
