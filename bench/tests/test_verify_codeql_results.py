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

    def test_third_party_findings_are_reported_without_changing_sarif(self) -> None:
        finding = self.located_finding("native/pcre2/pcre2-10.46/src/pcre2_compile.c")
        result = self.run_gate({"results": [finding]}, ["--third-party-root", "native/pcre2"])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("third-party source (reported unchanged)", result.stderr)
        self.assertIn("1 finding(s) in unchanged third-party source", result.stderr)
        self.assertIn("GitHub code scanning", result.stderr)

    def test_third_party_policy_does_not_accept_owned_unknown_or_escaping_locations(self) -> None:
        for source in (
            "native/entry/scout_main.c", "src/Scout.Pcre2/Pcre2Library.cs",
            "native/pcre2-extra/example.c", "native/pcre2/../../src/example.c",
            "native/pcre2/%2e%2e/%2e%2e/src/example.c", "/native/pcre2/example.c",
            "file:///native/pcre2/example.c",
        ):
            with self.subTest(source=source):
                finding = self.located_finding(source)
                result = self.run_gate({"results": [finding]}, ["--third-party-root", "native/pcre2"])
                self.assertNotEqual(0, result.returncode)
        for finding in (
            {"ruleId": "test/security", "message": {"text": "Unknown location"}},
            {**self.located_finding("native/pcre2/example.c"), "locations": [
                *self.located_finding("native/pcre2/example.c")["locations"],
                *self.located_finding("native/entry/scout_main.c")["locations"],
            ]},
        ):
            result = self.run_gate({"results": [finding]}, ["--third-party-root", "native/pcre2"])
            self.assertNotEqual(0, result.returncode)

    def test_third_party_findings_fail_without_explicit_ownership_policy(self) -> None:
        result = self.run_gate({"results": [self.located_finding("native/pcre2/example.c")]})
        self.assertNotEqual(0, result.returncode)

    def test_mixed_owned_and_third_party_findings_fail(self) -> None:
        result = self.run_gate({"results": [
            self.located_finding("native/pcre2/example.c"),
            self.located_finding("native/entry/scout_main.c"),
        ]}, ["--third-party-root", "native/pcre2"])
        self.assertNotEqual(0, result.returncode)
        self.assertIn("1 finding(s) in Scout-owned code", result.stderr)
        self.assertIn("1 finding(s) in unchanged third-party source", result.stderr)

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

    def located_finding(self, source: str) -> dict:
        return {"ruleId": "test/security", "message": {"text": "Finding"}, "locations": [
            {"physicalLocation": {"artifactLocation": {"uri": source}}},
        ]}

    def run_gate(self, run: dict, options: list[str] | None = None) -> subprocess.CompletedProcess[str]:
        return self.invoke({"scan.sarif": json.dumps({"version": "2.1.0", "runs": [run]})}, options)

    def invoke(self, documents: dict[str, str], options: list[str] | None = None) -> subprocess.CompletedProcess[str]:
        script = Path(__file__).resolve().parents[2] / "eng" / "verify-codeql-results.py"
        with tempfile.TemporaryDirectory() as directory:
            for relative_path, contents in documents.items():
                path = Path(directory) / relative_path
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(contents, encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(script), directory, *(options or [])],
                capture_output=True, text=True, check=False,
            )
            for relative_path, contents in documents.items():
                self.assertEqual(contents, (Path(directory) / relative_path).read_text(encoding="utf-8"))
            return result
