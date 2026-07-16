"""Shell-level tests for Hyperfine release-gate retry control flow."""

from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class HyperfineShellTests(unittest.TestCase):
    """Exercise the real run_pair_impl function with deterministic test doubles."""

    def test_word_boundary_gate_exercises_prefilter_free_count_lanes(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn(
            "alpha bravo charl delta eagle foxtt and unrelated symbols.", source
        )
        self.assertIn(
            "$RG_LINE_REGEX_PREFIX '\\\\b\\\\w{5}\\\\s+\\\\w{5}\\\\s+\\\\w{5}\\\\b'",
            source,
        )
        self.assertIn(
            "$SCOUT_LINE_REGEX_PREFIX "
            "'\\\\b\\\\w{5}\\\\s+\\\\w{5}\\\\s+\\\\w{5}\\\\b'",
            source,
        )
        self.assertIn(
            "$RG_LINE_REGEX_LINE_COUNT_PREFIX "
            "'\\\\b\\\\w{5}\\\\s+\\\\w{5}\\\\s+\\\\w{5}\\\\b'",
            source,
        )
        self.assertIn(
            "$SCOUT_LINE_REGEX_LINE_COUNT_PREFIX "
            "'\\\\b\\\\w{5}\\\\s+\\\\w{5}\\\\s+\\\\w{5}\\\\b'",
            source,
        )

    def test_issue_44_absent_pattern_gates_remain_in_the_release_suite(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('printf "issue44_absent_pattern_%03d\\n", i', source)
        self.assertIn('"many_absent_regexp_general"', source)
        self.assertIn('"many_absent_pattern_file_general"', source)
        self.assertIn("$RG_MANY_ABSENT_REGEXP_COMMAND", source)
        self.assertIn("$SCOUT_MANY_ABSENT_REGEXP_COMMAND", source)
        self.assertIn("$RG_MANY_ABSENT_PATTERN_FILE_COMMAND", source)
        self.assertIn("$SCOUT_MANY_ABSENT_PATTERN_FILE_COMMAND", source)

    def test_initial_failure_can_pass_on_first_retry(self) -> None:
        result = self._run(scenario="retry_pass", retries=2)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("-- sample_workload retry 1/2 --", result.stdout)
        self.assertNotIn("retry 2/2", result.stdout)
        self.assertEqual(2, result.stdout.count("report:"))

    def test_zero_retries_returns_the_initial_failure(self) -> None:
        result = self._run(scenario="always_fail", retries=0)

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertNotIn("retry", result.stdout)
        self.assertEqual(1, result.stdout.count("report:"))

    def test_final_failure_uses_singular_retry_context_and_nonzero_exit(self) -> None:
        result = self._run(scenario="always_fail", retries=1)

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertIn("-- sample_workload retry 1/1 --", result.stdout)
        self.assertIn(
            "report:sample_workload|1.500|/tmp/gate/sample_workload.retry-1.json|"
            "after the initial attempt and 1 retry",
            result.stdout,
        )
        self.assertEqual(2, result.stdout.count("report:"))

    def _run(self, scenario: str, retries: int) -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        start = source.index("run_pair_impl() {")
        end = source.index("\nrun_pair() {", start)
        function = source[start:end]
        harness = f"""#!/bin/sh
set -eu
MODE=gate
OUT_DIR=/tmp/gate
GATE_RETRY_FAILED_WORKLOADS={retries}
scenario={scenario}
report_calls=0
run_hyperfine_interleaved() {{
    printf 'sample:%s\\n' "$3"
}}
report_interleaved_gate() {{
    report_calls=$((report_calls + 1))
    printf 'report:%s|%s|%s|%s\\n' "$1" "$2" "$3" "${{4:-}}"
    if [ "$scenario" = retry_pass ] && [ "$report_calls" -gt 1 ]; then
        return 0
    fi
    return 1
}}
{function}
run_pair_impl sample_workload 1.500 'rg command' 'scout command' 6 6 0
"""

        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "retry-harness.sh"
            path.write_bytes(harness.encode("utf-8"))
            return subprocess.run(
                [_SH, str(path)],
                check=False,
                capture_output=True,
                text=True,
            )


if __name__ == "__main__":
    unittest.main()
