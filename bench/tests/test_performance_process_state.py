"""Behavioral tests for the canonical release-gate process state."""

from __future__ import annotations

import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")
_BASH = (
    str(Path(_SH).with_name("bash" + Path(_SH).suffix))
    if _SH and Path(_SH).with_name("bash" + Path(_SH).suffix).exists()
    else shutil.which("bash")
)


@unittest.skipUnless(_BASH, "requires Bash")
class PerformanceProcessStateTests(unittest.TestCase):
    """Exercise process-state normalization in isolated child shells."""

    def test_helper_has_valid_shell_syntax_and_stable_macos_tools(self) -> None:
        helper = self._helper()

        result = subprocess.run(
            [_BASH, "-n", str(helper)],
            check=False,
            capture_output=True,
            text=True,
        )
        source = helper.read_text(encoding="utf-8")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("#!/bin/bash", source.splitlines()[0])
        self.assertIn('/bin/ps -o nice= -p "$$"', source)
        self.assertIn("/usr/bin/awk", source)
        self.assertNotIn("${PERFORMANCE_PROCESS_", source)

    def test_clean_gate_configures_process_state_before_disposable_state(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        manifest = (root / "bench" / "run-hyperfine.sh").read_text(
            encoding="utf-8"
        )
        bootstrap_end = driver.index(
            '\nfi\n\n',
            driver.index('if [ "${SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED:-}"'),
        )
        source_index = driver.index(
            '. "$ROOT/eng/performance-process-state.sh"', bootstrap_end
        )
        configure_index = driver.index(
            "if ! configure_performance_process_state; then", source_index
        )
        state_index = driver.index('initialize_performance_state "/tmp"')

        self.assertLess(bootstrap_end, source_index)
        self.assertLess(source_index, configure_index)
        self.assertLess(configure_index, state_index)
        self.assertIn(
            "could not establish canonical process state", driver
        )
        self.assertIn(
            '${SCOUT_PERFORMANCE_GATE_RUNNER_NAME:-local}', manifest
        )
        self.assertIn('${SCOUT_PERFORMANCE_GATE_IMAGE_OS:-local}', manifest)
        self.assertIn(
            '${SCOUT_PERFORMANCE_GATE_IMAGE_VERSION:-local}', manifest
        )
        self.assertIn('${PERFORMANCE_PROCESS_UMASK:-unmanaged}', manifest)
        self.assertIn('${PERFORMANCE_PROCESS_SOFT_NOFILE:-unmanaged}', manifest)
        self.assertIn('${PERFORMANCE_PROCESS_NICE:-unmanaged}', manifest)
        self.assertIn(
            "process state: umask=%s; soft nofile=%s; nice=%s", manifest
        )
        manifest_start = manifest.index("print_repro_manifest() {")
        manifest_end = manifest.index("\nperformance_inputs_dirty() {", manifest_start)
        manifest_function = manifest[manifest_start:manifest_end]
        self.assertNotIn("${RUNNER_NAME", manifest_function)
        self.assertNotIn("${ImageOS", manifest_function)
        self.assertNotIn("${ImageVersion", manifest_function)

    def test_repro_manifest_prints_validated_metadata_and_process_state(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(
            encoding="utf-8"
        )
        function_start = source.index("print_repro_manifest() {")
        function_end = source.index("\nperformance_inputs_dirty() {", function_start)
        manifest_function = source[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            harness = Path(temporary_directory) / "manifest.sh"
            harness.write_text(
                "#!/bin/bash\n"
                "set -eu\n"
                f"{manifest_function}\n"
                "uname() { [ \"$1\" = -s ] && printf 'Darwin\\n' || printf 'arm64\\n'; }\n"
                "sw_vers() { [ \"$1\" = -productVersion ] && printf '26.3\\n' || printf 'build\\n'; }\n"
                "sysctl() { printf 'MacBookPro\\n'; }\n"
                "logical_cpu_count() { printf '12\\n'; }\n"
                "sha256_file() { printf 'sha\\n'; }\n"
                "read_corpus_value() { printf 'corpus-sha\\n'; }\n"
                "performance_inputs_dirty() { printf '0\\n'; }\n"
                "git() { printf 'commit\\n'; }\n"
                "sh() { printf 'fingerprint\\n'; }\n"
                "fake_rg() { printf 'ripgrep 15\\n'; }\n"
                "fake_scout() { printf 'scout 0.4.5\\n'; }\n"
                "fake_hyperfine() { printf 'hyperfine 1.20\\n'; }\n"
                f"ROOT={shlex.quote(str(root))}\n"
                "RG_BIN=fake_rg\nSCOUT_BIN=fake_scout\n"
                "SCOUT_RSS_BASELINE_BIN=/scout-real\n"
                "SCOUT_BUILD_PROVENANCE=/provenance\nHYPERFINE=fake_hyperfine\n"
                "PERFORMANCE_INPUT_MANIFEST=/generated-performance-inputs.json\n"
                f"OUT_DIR={shlex.quote(temporary_directory)}\n"
                f"PYTHON={shlex.quote(sys.executable)}\n"
                "HOST_ORACLE_ENVIRONMENT=github-actions\n"
                "HOST_TOOL_ENVIRONMENT=github-actions\n"
                "WORKLOAD=\nGATE_GENERATED_THREADS=1\nGATE_LARGE_FILE_THREADS=4\nGATE_TREE_THREADS=3\n"
                "OPENSUBTITLES_WARMUP=6\nOPENSUBTITLES_RUNS=6\n"
                "TREE_WARMUP=6\nTREE_RUNS=6\nCOLD_WARMUP=6\nCOLD_RUNS=6\n"
                "BOUNDED_ASSIGNMENT_WARMUP=6\nBOUNDED_ASSIGNMENT_RUNS=6\n"
                "LINE_REGEX_WARMUP=6\nLINE_REGEX_RUNS=6\n"
                "GATE_MANY_ABSENT_INPUT_COUNT=16\n"
                "SCOUT_SOURCE_COMMIT=commit\nSCOUT_SOURCE_FINGERPRINT=fingerprint\n"
                "SCOUT_SOURCE_DIRTY=0\nSCOUT_BUILD_DOTNET_SDK=10.0.102\n"
                "SCOUT_BUILD_DOTNET_HOST_RUNTIME=10.0.9\n"
                "SCOUT_BUILD_NATIVEAOT_RUNTIME=10.0.2\n"
                "SCOUT_BUILD_XCODE_VERSION=26.3\nSCOUT_BUILD_XCODE_BUILD=17C529\n"
                "SCOUT_BUILD_MACOS_SDK=26.2\n"
                "SCOUT_BUILD_MACOS_DEPLOYMENT_TARGET=14.0\n"
                "SCOUT_BUILD_COMPILER=clang\nSCOUT_BUILD_COMPILER_SHA256=compiler-sha\n"
                "SCOUT_BUILD_LINKER=ld\nSCOUT_BUILD_LINKER_SHA256=linker-sha\n"
                "SCOUT_BUILD_ARCHIVER_SHA256=archiver-sha\n"
                "SCOUT_BUILD_RANLIB_SHA256=ranlib-sha\n"
                "SCOUT_BUILD_STRIP_SHA256=strip-sha\n"
                "SCOUT_BUILD_NM_SHA256=nm-sha\n"
                "print_repro_manifest\n",
                encoding="utf-8",
            )
            environment = dict(os.environ)
            environment.update(
                {
                    "RUNNER_NAME": "stripped-runner",
                    "ImageOS": "stripped-os",
                    "ImageVersion": "stripped-version",
                    "SCOUT_PERFORMANCE_GATE_RUNNER_NAME": "GitHub Actions 42",
                    "SCOUT_PERFORMANCE_GATE_IMAGE_OS": "macos26",
                    "SCOUT_PERFORMANCE_GATE_IMAGE_VERSION": "20260715.1",
                    "PERFORMANCE_PROCESS_UMASK": "0022",
                    "PERFORMANCE_PROCESS_SOFT_NOFILE": "1024",
                    "PERFORMANCE_PROCESS_NICE": "0",
                }
            )

            result = subprocess.run(
                [_BASH, str(harness)],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            manifest_document = json.loads(
                (Path(temporary_directory) / "reproducibility.json").read_text(
                    encoding="utf-8"
                )
            )

        self.assertIn(
            "runner: GitHub Actions 42; image OS=macos26; "
            "image version=20260715.1",
            result.stdout,
        )
        self.assertIn(
            "process state: umask=0022; soft nofile=1024; nice=0",
            result.stdout,
        )
        self.assertIn(
            "reproducibility manifest:",
            result.stdout,
        )
        self.assertEqual(
            "20260715.1",
            manifest_document["values"]["runner.image_version"],
        )
        self.assertEqual(
            "0022", manifest_document["values"]["process.umask"]
        )
        self.assertNotIn("stripped-runner", result.stdout)
        self.assertNotIn("stripped-os", result.stdout)
        self.assertNotIn("stripped-version", result.stdout)

    def test_inherited_umask_is_normalized_to_0022(self) -> None:
        result = self._run_helper(
            "umask 0077\n"
            f"{self._process_state_test_seams()}"
            'configure_performance_process_state\n'
            "printf 'actual=%s\\nrecorded=%s\\n' "
            '"$(umask)" "$PERFORMANCE_PROCESS_UMASK"\n'
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["actual=0022", "recorded=0022"], result.stdout.splitlines())

    def test_inherited_soft_nofile_is_normalized_to_1024(self) -> None:
        result = self._run_helper(
            f"{self._process_state_test_seams('512')}"
            'configure_performance_process_state\n'
            "printf 'actual=%s\\nrecorded=%s\\n' "
            '"$(read_performance_soft_nofile)" '
            '"$PERFORMANCE_PROCESS_SOFT_NOFILE"\n'
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["actual=1024", "recorded=1024"], result.stdout.splitlines())

    def test_nice_output_verifier_normalizes_spacing_and_rejects_drift(self) -> None:
        accepted = self._run_helper("verify_performance_nice_output '   0  '\n")
        malformed = self._run_helper(
            "verify_performance_nice_output 'not-a-priority'\n"
        )
        nonzero = self._run_helper("verify_performance_nice_output '1'\n")

        self.assertEqual(0, accepted.returncode, accepted.stderr)
        self.assertEqual("0", accepted.stdout.strip())
        self.assertNotEqual(0, malformed.returncode)
        self.assertIn("parse the process nice priority", malformed.stderr)
        self.assertNotEqual(0, nonzero.returncode)
        self.assertIn("requires process nice priority 0", nonzero.stderr)

    def test_nice_reader_failure_is_reported_through_the_test_seam(self) -> None:
        result = self._run_helper(
            f"{self._process_state_test_seams()}"
            "read_performance_nice_output() { return 1; }\n"
            "configure_performance_process_state\n"
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("could not read its process nice priority", result.stderr)

    def test_inherited_environment_cannot_override_recorded_state(self) -> None:
        environment = dict(os.environ)
        environment.update(
            {
                "PERFORMANCE_PROCESS_UMASK": "0077",
                "PERFORMANCE_PROCESS_SOFT_NOFILE": "999999",
                "PERFORMANCE_PROCESS_NICE": "19",
            }
        )
        result = self._run_helper(
            f"{self._process_state_test_seams()}"
            'configure_performance_process_state\n'
            "printf '%s\\n' "
            '"$PERFORMANCE_PROCESS_UMASK" '
            '"$PERFORMANCE_PROCESS_SOFT_NOFILE" '
            '"$PERFORMANCE_PROCESS_NICE"\n',
            environment=environment,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(["0022", "1024", "0"], result.stdout.splitlines())

    @staticmethod
    def _helper() -> Path:
        return (
            Path(__file__).resolve().parents[2]
            / "eng"
            / "performance-process-state.sh"
        )

    def _run_helper(
        self,
        body: str,
        *,
        environment: dict[str, str] | None = None,
    ) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                _BASH,
                "-c",
                'set -eu\n. "$1"\n' + body,
                "sh",
                str(self._helper()),
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )

    @staticmethod
    def _process_state_test_seams(initial_nofile: str = "4096") -> str:
        return (
            f"performance_test_soft_nofile={initial_nofile}\n"
            "set_performance_soft_nofile() { "
            'performance_test_soft_nofile="$1"; }\n'
            "read_performance_soft_nofile() { "
            'printf \'%s\\n\' "$performance_test_soft_nofile"; }\n'
            "read_performance_nice_output() { printf '  0\\n'; }\n"
        )


if __name__ == "__main__":
    unittest.main()
