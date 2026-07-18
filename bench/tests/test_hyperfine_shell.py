"""Shell-level tests for Hyperfine release-gate control flow."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")
_GIT = shutil.which("git")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class HyperfineShellTests(unittest.TestCase):
    """Exercise the real run_pair_impl function with deterministic test doubles."""

    def test_complete_gate_delegates_to_the_release_equivalent_driver(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = root / "bench" / "run-hyperfine.sh"

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            temporary_bench = temporary_root / "bench"
            temporary_eng = temporary_root / "eng"
            temporary_bench.mkdir()
            temporary_eng.mkdir()
            temporary_script = temporary_bench / "run-hyperfine.sh"
            temporary_script.write_bytes(source.read_bytes())
            driver = temporary_eng / "run-performance-gate.sh"
            driver.write_text(
                "#!/bin/sh\nprintf '<%s>\\n' \"$@\"\n",
                encoding="utf-8",
            )
            driver.chmod(0o755)

            result = subprocess.run(
                [_SH, str(temporary_script), "--gate"],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("<--gate>", result.stdout.strip())

    def test_release_driver_isolates_and_sanitizes_the_native_build(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )

        self.assertIn('worktree add --detach "$PERFORMANCE_WORKTREE" HEAD', driver)
        self.assertIn('worktree remove --force "$PERFORMANCE_WORKTREE"', driver)
        self.assertIn('ln -s "$ROOT/artifacts/corpora"', driver)
        self.assertIn("copy_gate_aggregates", driver)
        self.assertIn("    CFLAGS \\\n", driver)
        self.assertIn("    LDFLAGS \\\n", driver)
        self.assertIn(
            'SCOUT_PERFORMANCE_GATE_INNER=1 '
            '"$PERFORMANCE_WORKTREE/bench/run-hyperfine.sh"',
            driver,
        )

    def test_release_driver_rejects_custom_sampling_before_host_setup(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = root / "eng" / "run-performance-gate.sh"

        result = subprocess.run(
            [_SH, str(driver), "--gate", "--runs", "2"],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertEqual(2, result.returncode)
        self.assertIn("accepts only --gate", result.stderr)
        self.assertNotIn("requires macOS arm64", result.stderr)

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

    def test_issue_37_exact_expressions_remain_in_the_release_suite(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('"line_regex_generated_record_word_boundary_general"', source)
        self.assertIn(
            "$RG_LINE_REGEX_PREFIX '\\\\bGeneratedRecord\\\\b'", source
        )
        self.assertIn(
            "$SCOUT_LINE_REGEX_PREFIX '\\\\bGeneratedRecord\\\\b'", source
        )
        self.assertIn('"line_regex_bounded_class_exact_general"', source)
        self.assertIn(
            'RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND="$(expect_no_match_command '
            '"$RG_LINE_REGEX_PREFIX \'^[A-Za-z_]{70,90}$\'',
            source,
        )
        self.assertIn(
            'SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND="$(expect_no_match_command '
            '"$SCOUT_LINE_REGEX_PREFIX \'^[A-Za-z_]{70,90}$\'',
            source,
        )
        self.assertIn('"$RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND"', source)
        self.assertIn('"$SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND"', source)
        self.assertIn("'^[A-Za-z_]{70,90}\\\\r?$'", source)

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
        self.assertIn('GATE_MANY_ABSENT_INPUT_COUNT="16"', source)
        self.assertIn("repeat_shell_argument", source)
        self.assertIn("$MANY_ABSENT_INPUTS", source)

    def test_short_shared_delegate_gate_disables_shell_calibration(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn(
            "run_pair_no_shell \\\n"
            '    "shared_delegate_prefix_general"',
            source,
        )
        self.assertIn(
            'SHARED_DELEGATE_INPUTS="$Q_LINE_REGEX_INPUT $Q_LINE_REGEX_INPUT '
            '$Q_LINE_REGEX_INPUT $Q_LINE_REGEX_INPUT"',
            source,
        )
        self.assertIn(
            "' $SHARED_DELEGATE_INPUTS\" \\",
            source,
        )

    def test_linux_tree_workloads_pin_three_threads_for_both_binaries(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('GATE_TREE_THREADS="3"', source)
        self.assertEqual(8, source.count("--threads $GATE_TREE_THREADS"))
        for workload in (
            "linux_recursive_literal",
            "linux_heldout_regex_general",
            "linux_heldout_capture_general",
            "linux_many_small_parallel",
        ):
            start = source.index(f'    "{workload}" \\\n')
            end = source.index("    \"$TREE_WARMUP\"", start)
            block = source[start:end]
            self.assertEqual(2, block.count("--threads $GATE_TREE_THREADS"))

    def test_generated_single_file_workloads_pin_one_thread(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('GATE_GENERATED_THREADS="1"', source)
        self.assertEqual(4, source.count("--threads $GATE_GENERATED_THREADS"))

    def test_source_fingerprint_is_stable_for_unchanged_content(self) -> None:
        root = Path(__file__).resolve().parents[2]
        helper = root / "eng" / "source-fingerprint.sh"

        first = subprocess.run(
            [_SH, str(helper)], check=True, capture_output=True, text=True
        ).stdout.strip()
        second = subprocess.run(
            [_SH, str(helper)], check=True, capture_output=True, text=True
        ).stdout.strip()

        self.assertEqual(first, second)
        self.assertEqual(40, len(first))
        self.assertTrue(all(character in "0123456789abcdef" for character in first))

    def test_source_fingerprint_handles_a_different_repository_owner(self) -> None:
        if not _GIT:
            self.skipTest("requires Git")

        root = Path(__file__).resolve().parents[2]
        helper = root / "eng" / "source-fingerprint.sh"
        environment = dict(os.environ)
        environment["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1"
        probe = subprocess.run(
            [_GIT, "-C", str(root), "rev-parse", "HEAD"],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )
        if probe.returncode == 0 or "dubious ownership" not in probe.stderr:
            self.skipTest("Git does not support different-owner simulation")

        result = subprocess.run(
            [_SH, str(helper)],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )
        fingerprint = result.stdout.strip()

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(40, len(fingerprint))
        self.assertTrue(
            all(character in "0123456789abcdef" for character in fingerprint)
        )

    def test_performance_harness_fingerprint_is_stable_for_unchanged_content(self) -> None:
        root = Path(__file__).resolve().parents[2]
        helper = root / "eng" / "performance-harness-fingerprint.sh"

        first = subprocess.run(
            [_SH, str(helper)], check=True, capture_output=True, text=True
        ).stdout.strip()
        second = subprocess.run(
            [_SH, str(helper)], check=True, capture_output=True, text=True
        ).stdout.strip()

        self.assertEqual(first, second)
        self.assertEqual(40, len(first))
        self.assertTrue(all(character in "0123456789abcdef" for character in first))

    def test_focused_gate_option_is_validated_before_prerequisites(self) -> None:
        root = Path(__file__).resolve().parents[2]
        script = root / "bench" / "run-hyperfine.sh"

        invalid = subprocess.run(
            [_SH, str(script), "--gate", "--workload", "not-a-workload"],
            check=False,
            capture_output=True,
            text=True,
        )
        wrong_mode = subprocess.run(
            [_SH, str(script), "--workload", "linux_heldout_capture_general"],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertNotEqual(0, invalid.returncode)
        self.assertIn("Unknown release-gate workload", invalid.stderr)
        self.assertNotEqual(0, wrong_mode.returncode)
        self.assertIn("--workload requires --gate", wrong_mode.stderr)

    def test_gate_defaults_to_the_hosted_oracle_locally(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        start = source.index("oracle_environment() {")
        end = source.index("\nread_lock_rid_table_value() {", start)
        function = source[start:end]
        harness = f"""#!/bin/sh
set -eu
fail() {{ printf '%s\\n' "$1" >&2; exit 1; }}
MODE=gate
{function}
oracle_environment
"""

        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "oracle-environment.sh"
            path.write_bytes(harness.encode("utf-8"))
            environment = dict(os.environ)
            environment.pop("SCOUT_ORACLE_ENVIRONMENT", None)
            result = subprocess.run(
                [_SH, str(path)],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("github-actions", result.stdout.strip())

    def test_resolved_environments_are_exported_to_subprocess_helpers(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        resolution = source.index('HOST_ORACLE_ENVIRONMENT="$(oracle_environment)"')
        oracle_export = source.index(
            'export SCOUT_ORACLE_ENVIRONMENT="$HOST_ORACLE_ENVIRONMENT"',
            resolution,
        )
        oracle_read = source.index(
            'RG_VALUE="$(read_ripgrep_oracle_value "path" "ripgrep_rg_path")"',
            resolution,
        )

        self.assertLess(resolution, oracle_export)
        self.assertLess(oracle_export, oracle_read)
        self.assertIn('export SCOUT_HOST_RID="$RID"', source)
        self.assertIn(
            'export SCOUT_TOOL_ENVIRONMENT="$HOST_TOOL_ENVIRONMENT"', source
        )

    def test_host_tools_select_the_environment_that_executes_the_gate(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        start = source.index("tool_environment() {")
        end = source.index("\nread_lock_rid_table_value() {", start)
        function = source[start:end]
        harness = f"""#!/bin/sh
set -eu
fail() {{ printf '%s\\n' "$1" >&2; exit 1; }}
{function}
tool_environment
"""

        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "tool-environment.sh"
            path.write_bytes(harness.encode("utf-8"))
            local_environment = dict(os.environ)
            local_environment.pop("GITHUB_ACTIONS", None)
            local_environment.pop("SCOUT_TOOL_ENVIRONMENT", None)
            hosted_environment = dict(local_environment)
            hosted_environment["GITHUB_ACTIONS"] = "true"
            local = subprocess.run(
                [_SH, str(path)],
                check=False,
                capture_output=True,
                text=True,
                env=local_environment,
            )
            hosted = subprocess.run(
                [_SH, str(path)],
                check=False,
                capture_output=True,
                text=True,
                env=hosted_environment,
            )

        self.assertEqual(0, local.returncode, local.stderr)
        self.assertEqual("local", local.stdout.strip())
        self.assertEqual(0, hosted.returncode, hosted.stderr)
        self.assertEqual("github-actions", hosted.stdout.strip())

    def test_unselected_workload_does_not_sample_or_report(self) -> None:
        result = self._run(scenario="fail", selected="other")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("", result.stdout)

    def test_failure_is_final_without_resampling(self) -> None:
        result = self._run(scenario="fail")

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertNotIn("retry", result.stdout)
        self.assertEqual(1, result.stdout.count("sample:"))
        self.assertEqual(1, result.stdout.count("report:"))

    def test_success_samples_once(self) -> None:
        result = self._run(scenario="pass")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, result.stdout.count("sample:"))
        self.assertEqual(1, result.stdout.count("report:"))

    def _run(self, scenario: str, selected: str = "") -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        start = source.index("run_pair_impl() {")
        end = source.index("\nrun_pair() {", start)
        function = source[start:end]
        harness = f"""#!/bin/sh
set -eu
MODE=gate
OUT_DIR=/tmp/gate
PYTHON=python_stub
ROOT=/tmp
WORKLOAD={selected!r}
scenario={scenario}
python_stub() {{
    return 0
}}
workload_selected() {{
    [ -z "$WORKLOAD" ] || [ "$WORKLOAD" = "$1" ]
}}
run_hyperfine_interleaved() {{
    printf 'sample:%s\\n' "$3"
}}
report_interleaved_gate() {{
    printf 'report:%s|%s|%s|%s\\n' "$1" "$2" "$3" "${{4:-}}"
    [ "$scenario" = pass ]
}}
{function}
run_pair_impl sample_workload 1.500 'rg command' 'scout command' 6 6 0
"""

        with tempfile.TemporaryDirectory() as temporary_directory:
            path = Path(temporary_directory) / "gate-harness.sh"
            path.write_bytes(harness.encode("utf-8"))
            return subprocess.run(
                [_SH, str(path)],
                check=False,
                capture_output=True,
                text=True,
            )


if __name__ == "__main__":
    unittest.main()
