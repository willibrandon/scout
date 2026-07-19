"""Shell-level tests for Hyperfine release-gate control flow."""

from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


_SH = shutil.which("sh")
_SH_SIBLING_BASH = (
    Path(_SH).with_name("bash" + Path(_SH).suffix) if _SH else None
)
_BASH = (
    str(_SH_SIBLING_BASH)
    if _SH_SIBLING_BASH and _SH_SIBLING_BASH.exists()
    else shutil.which("bash")
)
_GIT = shutil.which("git")


@unittest.skipUnless(_SH, "requires a POSIX shell")
class HyperfineShellTests(unittest.TestCase):
    """Exercise release-gate shell control flow with deterministic test doubles."""

    def test_every_gate_delegates_to_the_release_equivalent_driver(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = root / "bench" / "run-hyperfine.sh"

        source_text = source.read_text(encoding="utf-8")
        self.assertEqual("#!/bin/sh", source_text.splitlines()[0])
        self.assertIn('/usr/bin/dirname -- "$0"', source_text)

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

            complete = subprocess.run(
                [_SH, str(temporary_script), "--gate"],
                check=False,
                capture_output=True,
                text=True,
            )
            focused = subprocess.run(
                [
                    _SH,
                    str(temporary_script),
                    "--gate",
                    "--workload",
                    "line_regex_word_boundary_general",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertEqual(0, complete.returncode, complete.stderr)
        self.assertEqual("<--gate>", complete.stdout.strip())
        self.assertEqual(0, focused.returncode, focused.stderr)
        self.assertEqual(
            "<--gate>\n<--workload>\n<line_regex_word_boundary_general>",
            focused.stdout.strip(),
        )

    @unittest.skipUnless(_BASH, "requires Bash")
    def test_complete_gate_does_not_forward_bash_startup_hooks(self) -> None:
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
            driver.write_text("#!/bin/bash\nexit 0\n", encoding="utf-8")
            driver.chmod(0o755)
            startup_log = temporary_root / "bash-startup.log"
            startup_hook = temporary_root / "bash-startup.sh"
            startup_hook.write_text(
                'printf "startup\\n" >> "$BASH_ENV_LOG"\n', encoding="utf-8"
            )
            environment = dict(os.environ)
            environment["BASH_ENV"] = str(startup_hook)
            environment["BASH_ENV_LOG"] = str(startup_log)

            result = subprocess.run(
                [_BASH, str(temporary_script), "--gate"],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(
                ["startup"],
                startup_log.read_text(encoding="utf-8").splitlines(),
            )

    def test_release_driver_isolates_and_sanitizes_the_native_build(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )

        self.assertEqual("#!/bin/bash", driver.splitlines()[0])
        self.assertIn('/usr/bin/dirname -- "$0"', driver)
        self.assertEqual(3, driver.count("pwd -P"))
        self.assertIn("/usr/bin/uname -s", driver)
        self.assertNotIn("type -P dotnet", driver)
        self.assertIn(
            '"$ROOT/eng/setup-dotnet-performance-sdk.sh" "$DOTNET_ROOT"',
            driver,
        )
        setup_sdk_index = driver.index("setup-dotnet-performance-sdk.sh")
        self.assertLess(setup_sdk_index, driver.index("sanitize_performance_environment"))
        self.assertLess(setup_sdk_index, driver.index("dotnet restore"))
        self.assertIn('worktree add --detach "$PERFORMANCE_WORKTREE" HEAD', driver)
        self.assertIn('worktree remove --force "$PERFORMANCE_WORKTREE"', driver)
        self.assertNotIn('ln -s "$ROOT/artifacts/corpora"', driver)
        self.assertIn('"$PERFORMANCE_WORKTREE/eng/fetch-corpora.sh" --all', driver)
        self.assertIn("artifacts/corpora/opensubtitles", driver)
        self.assertIn("artifacts/corpora/linux", driver)
        self.assertNotIn("artifacts/nuget/packages", driver)
        self.assertIn("copy_gate_aggregates", driver)
        self.assertIn('"$source_directory"/*.samples', driver)
        self.assertIn('"$GATE_AGGREGATE_DIR"/*.samples', driver)
        self.assertIn('rm -rf "$previous_samples"', driver)
        self.assertIn('"$prefix"*)', driver)
        self.assertIn("remove_generated_directory", driver)
        self.assertIn('initialize_performance_state "/tmp"', driver)
        self.assertIn('performance-environment.sh"', driver)
        self.assertIn("sanitize_performance_environment", driver)
        sanitize_index = driver.index("sanitize_performance_environment")
        self.assertLess(sanitize_index, driver.index('export SCOUT_HOST_RID="osx-arm64"'))
        self.assertLess(
            sanitize_index,
            driver.index('export SCOUT_ORACLE_ENVIRONMENT="github-actions"'),
        )
        self.assertLess(
            sanitize_index,
            driver.index('export SCOUT_TOOL_ENVIRONMENT="$PERFORMANCE_TOOL_ENVIRONMENT"'),
        )
        self.assertIn(
            '"$ROOT/eng/setup-hyperfine.sh" '
            '"$PERFORMANCE_STATE_PARENT/hyperfine"',
            driver,
        )
        self.assertIn('export SCOUT_HYPERFINE_BIN', driver)
        hyperfine_setup_index = driver.index("setup-hyperfine.sh")
        self.assertLess(sanitize_index, hyperfine_setup_index)
        self.assertLess(hyperfine_setup_index, driver.index("worktree add --detach"))
        self.assertIn(
            'export NUGET_PACKAGES="$PERFORMANCE_STATE_PARENT/nuget/packages"',
            driver,
        )
        worktree_cd_index = driver.index('cd "$PERFORMANCE_WORKTREE"')
        self.assertLess(
            worktree_cd_index,
            driver.index('"$PERFORMANCE_WORKTREE/eng/fetch-corpora.sh" --all'),
        )
        self.assertLess(worktree_cd_index, driver.index("dotnet restore"))
        self.assertIn(
            'SCOUT_PERFORMANCE_GATE_INNER=1 '
            '"$PERFORMANCE_WORKTREE/bench/run-hyperfine.sh"',
            driver,
        )

    def test_release_driver_copies_raw_round_samples_back(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        function_start = driver.index("copy_gate_aggregates() {")
        function_end = driver.index("\ncleanup() {", function_start)
        copy_function = driver[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            worktree = temporary_root / "worktree"
            source = worktree / "artifacts" / "bench" / "hyperfine"
            samples = source / "example.samples"
            destination = temporary_root / "results"
            samples.mkdir(parents=True)
            (source / "example.json").write_text("{}", encoding="utf-8")
            (samples / "round-1.json").write_text("{}", encoding="utf-8")
            harness = temporary_root / "copy-results.sh"
            harness.write_text(
                "#!/bin/sh\n"
                "set -eu\n"
                f"{copy_function}\n"
                'PERFORMANCE_WORKTREE="$1"\n'
                'GATE_AGGREGATE_DIR="$2"\n'
                "copy_gate_aggregates\n",
                encoding="utf-8",
            )

            result = subprocess.run(
                [_SH, str(harness), str(worktree), str(destination)],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(
                "{}", (destination / "example.json").read_text(encoding="utf-8")
            )
            self.assertEqual(
                "{}",
                (destination / "example.samples" / "round-1.json").read_text(
                    encoding="utf-8"
                ),
            )

    def test_private_hyperfine_override_is_validated_then_consumed(self) -> None:
        root = Path(__file__).resolve().parents[2]
        gate = (root / "bench" / "run-hyperfine.sh").read_text(
            encoding="utf-8"
        )
        preflight = (root / "eng" / "preflight.sh").read_text(
            encoding="utf-8"
        )
        workflow = (
            root / ".github" / "workflows" / "release-gates.yml"
        ).read_text(encoding="utf-8")

        resolve_start = gate.index("resolve_hyperfine() {")
        resolve_end = gate.index("\nresolve_python() {", resolve_start)
        resolve = gate[resolve_start:resolve_end]
        self.assertIn('configured_path="${SCOUT_HYPERFINE_BIN:-}"', resolve)
        self.assertIn("SCOUT_HYPERFINE_BIN must be an absolute path", resolve)
        self.assertIn(
            'check_file_hash "hyperfine" "$configured_path" "$pinned_sha256"',
            resolve,
        )
        self.assertIn(
            'check_tool_version "hyperfine" "$configured_path" '
            '"hyperfine $pinned_version"',
            resolve,
        )
        resolved_index = gate.index('HYPERFINE="$(resolve_hyperfine)"')
        unset_index = gate.index("unset SCOUT_HYPERFINE_BIN", resolved_index)
        self.assertLess(resolved_index, unset_index)

        override_start = preflight.index(
            'if [ -n "${SCOUT_HYPERFINE_BIN:-}" ]; then'
        )
        override_end = preflight.index(
            '\n    if [ "$MACOS_TOOL_FAILURES"', override_start
        )
        override = preflight[override_start:override_end]
        self.assertIn("SCOUT_HYPERFINE_BIN must be an absolute path", override)
        self.assertIn(
            'check_file_hash "macOS tool hyperfine" "$HYPERFINE_PATH" '
            '"$HYPERFINE_SHA256"',
            override,
        )
        self.assertIn(
            'expect_equal "hyperfine version" "hyperfine $HYPERFINE_VERSION"',
            override,
        )
        self.assertNotIn("eng/setup-hyperfine.sh", workflow)

    def test_release_gate_upload_retains_aggregate_and_round_json(self) -> None:
        root = Path(__file__).resolve().parents[2]
        workflow = (
            root / ".github" / "workflows" / "release-gates.yml"
        ).read_text(encoding="utf-8")
        upload_start = workflow.index("- name: Upload hyperfine gate aggregates")
        upload_end = workflow.index("\n\n  native-linux-x64:", upload_start)
        upload = workflow[upload_start:upload_end]

        self.assertIn("artifacts/bench/hyperfine/*.json", upload)
        self.assertIn("artifacts/bench/hyperfine/*.samples/*.json", upload)

    def test_release_driver_removes_a_partial_generated_worktree(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        function_start = driver.index("initialize_performance_state() {")
        function_end = driver.index("\ntrap cleanup EXIT", function_start)
        cleanup_functions = driver[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            state_parent = temporary_root / "scout-performance-state.partial"
            parent = state_parent / "tmp" / "scout-performance-gate.partial"
            worktree = parent / "source"
            worktree.mkdir(parents=True)
            (worktree / "partial-output").write_text("partial", encoding="utf-8")
            destination = temporary_root / "results"
            harness = temporary_root / "cleanup-partial.sh"
            harness.write_text(
                "#!/bin/sh\n"
                "set -eu\n"
                f"{cleanup_functions}\n"
                'PERFORMANCE_WORKTREE="$1"\n'
                'PERFORMANCE_WORKTREE_PARENT="$2"\n'
                'PERFORMANCE_WORKTREE_PARENT_PREFIX="$3"\n'
                'GATE_AGGREGATE_DIR="$4"\n'
                'PERFORMANCE_STATE_PARENT="$5"\n'
                'PERFORMANCE_STATE_PARENT_PREFIX="$6"\n'
                "cleanup\n",
                encoding="utf-8",
            )

            result = subprocess.run(
                [
                    _SH,
                    str(harness),
                    str(worktree),
                    str(parent),
                    str(state_parent / "tmp" / "scout-performance-gate."),
                    str(destination),
                    str(state_parent),
                    str(temporary_root / "scout-performance-state."),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertFalse(parent.exists())
            self.assertFalse(state_parent.exists())

    def test_release_driver_refuses_an_unexpected_cleanup_path(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        function_start = driver.index("remove_generated_directory() {")
        function_end = driver.index("\ncopy_gate_aggregates() {", function_start)
        remove_function = driver[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            unexpected = temporary_root / "keep"
            unexpected.mkdir()
            harness = temporary_root / "refuse-cleanup.sh"
            harness.write_text(
                "#!/bin/bash\n"
                f"{remove_function}\n"
                'remove_generated_directory "$1" "$2" "test directory"\n',
                encoding="utf-8",
            )

            result = subprocess.run(
                [
                    _SH,
                    str(harness),
                    str(unexpected),
                    str(temporary_root / "expected."),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(0, result.returncode)
            self.assertIn("Refusing to remove unexpected", result.stderr)
            self.assertTrue(unexpected.exists())

    def test_release_driver_creates_fresh_outer_process_state(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        function_start = driver.index("initialize_performance_state() {")
        function_end = driver.index("\nremove_generated_directory() {", function_start)
        initialize_function = driver[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            harness = temporary_root / "initialize-state.sh"
            harness.write_text(
                "#!/bin/bash\n"
                "set -eu\n"
                f"{initialize_function}\n"
                'initialize_performance_state "$1"\n'
                "printf '%s\\n' \"$PERFORMANCE_STATE_PARENT\" \"$HOME\" "
                '"$TMPDIR" "$XDG_CACHE_HOME" "$XDG_CONFIG_HOME" '
                '"$XDG_DATA_HOME" "$XDG_STATE_HOME" "$DOTNET_CLI_HOME" '
                '"$PYTHONNOUSERSITE"\n',
                encoding="utf-8",
            )

            result = subprocess.run(
                [_SH, str(harness), str(temporary_root)],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            values = result.stdout.splitlines()
            self.assertEqual(9, len(values))
            state = Path(values[0])
            self.assertEqual(state / "home", Path(values[1]))
            self.assertEqual(state / "tmp", Path(values[2]))
            self.assertEqual(state / "xdg" / "cache", Path(values[3]))
            self.assertEqual(state / "xdg" / "config", Path(values[4]))
            self.assertEqual(state / "xdg" / "data", Path(values[5]))
            self.assertEqual(state / "xdg" / "state", Path(values[6]))
            self.assertEqual(state / "home", Path(values[7]))
            self.assertEqual("1", values[8])

    def test_release_driver_reexecutes_with_an_allowlisted_environment(self) -> None:
        root = Path(__file__).resolve().parents[2]
        environment_helper = root / "eng" / "performance-environment.sh"
        environment = dict(os.environ)
        environment.update(
            {
                "IlcOptimizationPreference": "poison-ilc",
                "MSBuildProjectExtensionsPath": "poison-msbuild",
                "SCOUT_ARBITRARY_POISON": "poison-scout",
                "SCOUT_HYPERFINE_BIN": "/poison/hyperfine",
                "SOURCE_DATE_EPOCH": "poison-source-date",
            }
        )

        result = subprocess.run(
            [
                _SH,
                "-c",
                '. "$1"; exec_clean_performance_gate '
                '"local" "$2" -c env',
                "sh",
                str(environment_helper),
                _SH,
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        clean = dict(
            line.split("=", 1) for line in result.stdout.splitlines() if "=" in line
        )
        self.assertNotIn("IlcOptimizationPreference", clean)
        self.assertNotIn("MSBuildProjectExtensionsPath", clean)
        self.assertNotIn("SCOUT_ARBITRARY_POISON", clean)
        self.assertNotIn("SCOUT_HYPERFINE_BIN", clean)
        self.assertNotIn("SOURCE_DATE_EPOCH", clean)
        self.assertEqual("C", clean["LANG"])
        self.assertEqual("C", clean["LC_ALL"])
        self.assertEqual(
            "/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin",
            clean["PATH"],
        )
        self.assertEqual("1", clean["SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED"])
        self.assertNotIn("SCOUT_PERFORMANCE_GATE_DOTNET_COMMAND", clean)
        self.assertEqual(
            "local", clean["SCOUT_PERFORMANCE_GATE_TOOL_ENVIRONMENT"]
        )
        self.assertEqual("UTC", clean["TZ"])

    def test_release_driver_sanitizes_build_environment_behaviorally(self) -> None:
        root = Path(__file__).resolve().parents[2]
        sanitizer = root / "eng" / "performance-environment.sh"
        sanitizer_text = sanitizer.read_text(encoding="utf-8")
        self.assertIn("/usr/bin/env | /usr/bin/sed", sanitizer_text)
        environment = dict(os.environ)
        poisoned = {
            "ACTIONS_CACHE_URL": "poison-actions-cache",
            "AR": "poison-ar",
            "BASH_ENV": "poison-bash-env",
            "CC": "poison-cc",
            "CFLAGS": "poison-cflags",
            "CI": "true",
            "COMPlus_GCHeapCount": "99",
            "CURL_HOME": "poison-curl-home",
            "DOTNET_PROCESSOR_COUNT": "99",
            "GIT_DIR": "poison-git-dir",
            "GITHUB_ACTIONS": "true",
            "HOMEBREW_PREFIX": "poison-homebrew",
            "MSBuildSDKsPath": "poison-msbuild",
            "MallocNanoZone": "poison-malloc",
            "NUGET_PACKAGES": "poison-nuget",
            "PYTHONINSPECT": "poison-python-inspect",
            "PYTHONPATH": "poison-python",
            "RUNNER_NAME": "poison-runner",
            "RestorePackagesPath": "poison-restore",
            "SCOUT_ARBITRARY_POISON": "poison-scout",
            "SDKROOT": "poison-sdk",
            "VIRTUAL_ENV": "poison-virtual-env",
        }
        environment.update(poisoned)
        environment["LANG"] = "poison-language"
        environment["LC_ALL"] = "poison-locale"
        environment["TZ"] = "poison-timezone"
        result = subprocess.run(
            [
                _SH,
                "-c",
                '. "$1"; sanitize_performance_environment "$2"; env',
                "sh",
                str(sanitizer),
                "/canonical/dotnet",
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        sanitized = dict(
            line.split("=", 1) for line in result.stdout.splitlines() if "=" in line
        )
        self.assertTrue(set(poisoned).isdisjoint(sanitized))
        self.assertEqual(
            "/canonical/dotnet:/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin",
            sanitized["PATH"],
        )
        self.assertEqual("/canonical/dotnet", sanitized["DOTNET_ROOT"])
        self.assertEqual("1", sanitized["DOTNET_CLI_TELEMETRY_OPTOUT"])
        self.assertEqual("1", sanitized["DOTNET_NOLOGO"])
        self.assertEqual("1", sanitized["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"])
        self.assertEqual("/dev/null", sanitized["GIT_CONFIG_GLOBAL"])
        self.assertEqual("1", sanitized["GIT_CONFIG_NOSYSTEM"])
        self.assertEqual("1", sanitized["HOMEBREW_NO_ANALYTICS"])
        self.assertEqual("1", sanitized["HOMEBREW_NO_AUTO_UPDATE"])
        self.assertEqual("1", sanitized["HOMEBREW_NO_ENV_HINTS"])
        self.assertEqual("C", sanitized["LANG"])
        self.assertEqual("C", sanitized["LC_ALL"])
        self.assertEqual("1", sanitized["PYTHONDONTWRITEBYTECODE"])
        self.assertEqual("1", sanitized["PYTHONNOUSERSITE"])
        self.assertEqual("1", sanitized["PYTHONSAFEPATH"])
        self.assertEqual("UTC", sanitized["TZ"])

    def test_release_gate_pins_and_isolates_the_performance_dotnet_sdk(self) -> None:
        root = Path(__file__).resolve().parents[2]
        prerequisite_lock = (root / "tests" / "PREREQS.lock").read_text(
            encoding="utf-8"
        )
        setup = (root / "eng" / "setup-dotnet-performance-sdk.sh").read_text(
            encoding="utf-8"
        )
        driver = (root / "eng" / "run-performance-gate.sh").read_text(
            encoding="utf-8"
        )
        environment_helper = (
            root / "eng" / "performance-environment.sh"
        ).read_text(encoding="utf-8")
        workflow = (
            root / ".github" / "workflows" / "release-gates.yml"
        ).read_text(encoding="utf-8")
        performance_job_start = workflow.index("  performance-gate:\n")
        performance_job_end = workflow.index(
            "\n  native-linux-x64:", performance_job_start
        )
        performance_job = workflow[performance_job_start:performance_job_end]

        self.assertIn('dotnet_sdk = "10.0.102"', prerequisite_lock)
        self.assertIn('dotnet_host_runtime = "10.0.10"', prerequisite_lock)
        self.assertIn(
            'nativeaot_runtime_framework = "10.0.2"', prerequisite_lock
        )
        self.assertIn('[[dotnet_sdk_archive]]', prerequisite_lock)
        self.assertIn('[[dotnet_runtime_archive]]', prerequisite_lock)
        self.assertIn('rid = "osx-arm64"', prerequisite_lock)
        self.assertIn(
            'url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/'
            '10.0.102/dotnet-sdk-10.0.102-osx-arm64.tar.gz"',
            prerequisite_lock,
        )
        self.assertIn(
            'sha512 = "5adb12a72ccfd327fe94ce99104ee7b9b56dbe40e354440a0b28313a4996ff34'
            'cc8560d605c1f30c247d364ae429de55d8c3b30ea19da04a716a059eb62b98ed"',
            prerequisite_lock,
        )
        self.assertIn(
            'url = "https://builds.dotnet.microsoft.com/dotnet/Runtime/'
            '10.0.10/dotnet-runtime-10.0.10-osx-arm64.tar.gz"',
            prerequisite_lock,
        )
        self.assertIn(
            'sha512 = "79cbc64bfeb806d5f2a9e0a2a2ed336c7aa275b0438bbd88d36236a1b6203950'
            '546b49ff307cc5067c89434ffe22c021a594b2f8adad71146a5ece825652bd85"',
            prerequisite_lock,
        )
        self.assertIn("/usr/bin/curl", setup)
        self.assertIn("verify_archive_sha512", setup)
        self.assertIn(
            'rm -rf -- "$EXTRACTED_ROOT/host/fxr" "$EXTRACTED_ROOT/shared"',
            setup,
        )
        self.assertIn("verify_sdk \"$EXTRACTED_ROOT\"", setup)
        self.assertIn("verify_sdk \"$INSTALL_ROOT\"", setup)
        self.assertIn('DOTNET_ROOT="$PERFORMANCE_STATE_PARENT/dotnet"', driver)
        self.assertNotIn("type -P dotnet", driver)
        self.assertNotIn("SCOUT_PERFORMANCE_GATE_DOTNET_COMMAND", driver)
        self.assertNotIn("SCOUT_PERFORMANCE_GATE_DOTNET_COMMAND", environment_helper)
        self.assertNotIn("actions/setup-dotnet", performance_job)
        self.assertIn("run: bench/run-hyperfine.sh --gate", performance_job)

    def test_pinned_performance_dotnet_sdk_inventory_is_verified_behaviorally(
        self,
    ) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "eng" / "setup-dotnet-performance-sdk.sh").read_text(
            encoding="utf-8"
        )
        function_start = source.index("verify_sdk() {")
        function_end = source.index("\nverify_archive_sha512() {", function_start)
        verify_function = source[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            dotnet_root = temporary_root / "dotnet-root"
            (dotnet_root / "sdk" / "10.0.102").mkdir(parents=True)
            (dotnet_root / "host" / "fxr" / "10.0.10").mkdir(parents=True)
            (
                dotnet_root
                / "shared"
                / "Microsoft.NETCore.App"
                / "10.0.10"
            ).mkdir(parents=True)
            fake_dotnet = dotnet_root / "dotnet"
            fake_dotnet.write_text(
                "#!/bin/sh\n"
                'root="$(dirname -- "$0")"\n'
                'runtime="${FAKE_DOTNET_RUNTIME:-10.0.10}"\n'
                'sdk_path="${FAKE_DOTNET_SDK_PATH:-$root/sdk}"\n'
                'runtime_path="${FAKE_DOTNET_RUNTIME_PATH:-$root/shared/Microsoft.NETCore.App}"\n'
                'base_path="${FAKE_DOTNET_BASE_PATH:-$root/sdk/10.0.102/}"\n'
                'case "$1" in\n'
                "  --version) printf '10.0.102\\n' ;;\n"
                "  --list-sdks) printf '10.0.102 [%s]\\n' \"$sdk_path\" ;;\n"
                "  --info) printf '.NET SDK:\\n Base Path: %s\\nHost:\\n  Version:      %s\\n  Architecture: arm64\\n' "
                '"$base_path" "$runtime" ;;\n'
                "  --list-runtimes) printf 'Microsoft.NETCore.App %s [%s]\\n' "
                '"$runtime" "$runtime_path" ;;\n'
                "  *) exit 2 ;;\n"
                "esac\n",
                encoding="utf-8",
            )
            fake_dotnet.chmod(0o755)
            harness = temporary_root / "verify-sdk.sh"
            harness.write_text(
                "#!/bin/sh\n"
                "set -eu\n"
                "fail() { printf '%s\\n' \"$1\" >&2; return 1; }\n"
                "EXPECTED_SDK=10.0.102\n"
                "EXPECTED_HOST_RUNTIME=10.0.10\n"
                f"{verify_function}\n"
                'verify_sdk "$1"\n',
                encoding="utf-8",
            )

            success = subprocess.run(
                [_SH, str(harness), str(dotnet_root)],
                check=False,
                capture_output=True,
                text=True,
            )
            bad_environment = dict(os.environ)
            bad_environment["FAKE_DOTNET_RUNTIME"] = "10.0.11"
            mismatch = subprocess.run(
                [_SH, str(harness), str(dotnet_root)],
                check=False,
                capture_output=True,
                text=True,
                env=bad_environment,
            )
            outside_sdk = temporary_root / "outside-sdk"
            outside_sdk.mkdir()
            outside_environment = dict(os.environ)
            outside_environment["FAKE_DOTNET_SDK_PATH"] = str(outside_sdk)
            outside = subprocess.run(
                [_SH, str(harness), str(dotnet_root)],
                check=False,
                capture_output=True,
                text=True,
                env=outside_environment,
            )

        self.assertEqual(0, success.returncode, success.stderr)
        self.assertNotEqual(0, mismatch.returncode)
        self.assertIn("expected 10.0.10, found 10.0.11", mismatch.stderr)
        self.assertNotEqual(0, outside.returncode)
        self.assertIn("SDK inventory escaped the isolated root", outside.stderr)

    def test_pinned_performance_dotnet_archive_hash_is_verified_behaviorally(
        self,
    ) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "eng" / "setup-dotnet-performance-sdk.sh").read_text(
            encoding="utf-8"
        )
        function_start = source.index("verify_archive_sha512() {")
        function_end = source.index('\n\n[ "$#" -eq 1 ]', function_start)
        verify_function = source[function_start:function_end]

        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_root = Path(temporary_directory)
            archive = temporary_root / "archive.tar.gz"
            archive.write_bytes(b"pinned archive fixture")
            expected = hashlib.sha512(archive.read_bytes()).hexdigest()
            harness = temporary_root / "verify-archive.sh"
            harness.write_text(
                "#!/bin/sh\n"
                "set -eu\n"
                "fail() { printf '%s\\n' \"$1\" >&2; return 1; }\n"
                f"{verify_function}\n"
                'verify_archive_sha512 "$1" "$2" ".NET SDK"\n',
                encoding="utf-8",
            )
            success = subprocess.run(
                [_SH, str(harness), str(archive), expected],
                check=False,
                capture_output=True,
                text=True,
            )
            mismatch = subprocess.run(
                [_SH, str(harness), str(archive), "0" * 128],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertEqual(0, success.returncode, success.stderr)
        self.assertNotEqual(0, mismatch.returncode)
        self.assertIn("archive SHA-512 mismatch", mismatch.stderr)

    def test_native_publish_uses_one_serialized_rid_aware_publish(
        self,
    ) -> None:
        root = Path(__file__).resolve().parents[2]
        publish_helper = root / "native" / "publish-app-unix.sh"

        with tempfile.TemporaryDirectory() as temporary_directory:
            invocation_log = Path(temporary_directory) / "dotnet-invocations.tsv"
            checkout = Path(temporary_directory) / "checkout with spaces"
            assets_file = checkout / "src" / "Scout.App" / "obj" / "project.assets.json"
            assets_file.parent.mkdir(parents=True)
            assets_file.write_text(
                """{
  "project": {
    "frameworks": {
      "net10.0": {
        "downloadDependencies": [
          {
            "name": "Microsoft.NETCore.App.Runtime.NativeAOT.osx-arm64",
            "version": "[10.0.42-test, 10.0.42-test]"
          }
        ]
      }
    }
  }
}
""",
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    _SH,
                    "-c",
                    """
dotnet() {
    dotnet_command="$1"
    {
        printf '%s' "$1"
        shift
        for argument in "$@"; do
            printf '\\t%s' "$argument"
        done
        printf '\\n'
    } >> "$DOTNET_INVOCATION_LOG"
    if [ "$dotnet_command" = "msbuild" ]; then
        printf '10.0.42-test\\n'
    fi
}
export DOTNET_INVOCATION_LOG="$2"
. "$1"
publish_native_app "$3" "osx-arm64" "0.4.5" "/output with spaces"
printf 'runtime=%s\\n' "$NATIVEAOT_RUNTIME_FRAMEWORK_VERSION"
""",
                    "sh",
                    str(publish_helper),
                    str(invocation_log),
                    str(checkout),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            invocations = [
                line.split("\t") for line in invocation_log.read_text().splitlines()
            ]

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("runtime=10.0.42-test", result.stdout.strip())
        self.assertEqual(
            ["publish", "msbuild"],
            [row[0] for row in invocations],
        )

        publish = invocations[0]
        self.assertEqual(
            f"{checkout}/src/Scout.App/Scout.App.csproj",
            publish[1],
        )
        self.assertIn("-r", publish)
        self.assertEqual("osx-arm64", publish[publish.index("-r") + 1])
        self.assertIn("-p:NativeLib=Static", publish)
        self.assertIn("-p:RestoreDisableParallel=true", publish)
        self.assertIn("-m:1", publish)
        self.assertIn("-o", publish)
        self.assertEqual("/output with spaces", publish[publish.index("-o") + 1])
        self.assertIn("--disable-build-servers", publish)
        self.assertNotIn("--no-restore", publish)
        self.assertNotIn("-p:PublishAot=true", publish)
        self.assertNotIn("-p:_IsPublishing=true", publish)

        evaluation = invocations[1]
        self.assertIn("-getProperty:RuntimeFrameworkVersion", evaluation)
        self.assertIn("-p:RuntimeIdentifier=osx-arm64", evaluation)

    def test_native_publish_rejects_ambiguous_runtime_framework_version(self) -> None:
        root = Path(__file__).resolve().parents[2]
        publish_helper = root / "native" / "publish-app-unix.sh"

        result = subprocess.run(
            [
                _SH,
                "-c",
                """
dotnet() {
    if [ "$1" = "msbuild" ]; then
        printf '10.0.2\\nunexpected-output\\n'
    fi
}
. "$1"
publish_native_app "/checkout" "osx-arm64" "0.4.5" "/output"
""",
                "sh",
                str(publish_helper),
            ],
            check=False,
            capture_output=True,
            text=True,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertIn("Expected one evaluated RuntimeFrameworkVersion", result.stderr)

    def test_xcode_selector_requires_the_exact_version_and_build(self) -> None:
        root = Path(__file__).resolve().parents[2]
        toolchain = root / "native" / "toolchain-unix.sh"

        with tempfile.TemporaryDirectory() as temporary_directory:
            applications = Path(temporary_directory)
            for application in (
                "Xcode_26.3.app",
                "Xcode_26.3.0.app",
                "Xcode.app",
            ):
                (applications / application / "Contents" / "Developer").mkdir(
                    parents=True
                )

            result = subprocess.run(
                [
                    _SH,
                    "-c",
                    """
. "$1"
native_xcode_identity() {
    case "$1" in
        *Xcode_26.3.app*)
            printf 'Xcode 26.3\\nBuild version wrong-build\\n'
            ;;
        *Xcode_26.3.0.app*)
            printf 'Xcode 26.3\\nBuild version 17C529\\n'
            ;;
        *Xcode.app*)
            printf 'Xcode 26.3\\nBuild version 17C529\\n'
            ;;
    esac
}
select_native_xcode_developer_dir 26.3 17C529 "$2"
""",
                    "sh",
                    str(toolchain),
                    str(applications),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(
                applications
                / "Xcode_26.3.0.app"
                / "Contents"
                / "Developer",
                Path(result.stdout.strip()),
            )

            mismatch = subprocess.run(
                [
                    _SH,
                    "-c",
                    """
. "$1"
native_xcode_identity() {
    printf 'Xcode 26.3\\nBuild version wrong-build\\n'
}
select_native_xcode_developer_dir 26.3 17C529 "$2"
""",
                    "sh",
                    str(toolchain),
                    str(applications),
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(0, mismatch.returncode)
            self.assertIn("Xcode 26.3 build 17C529 is required", mismatch.stderr)

    def test_native_build_uses_the_validated_toolchain_end_to_end(self) -> None:
        root = Path(__file__).resolve().parents[2]
        native_build = (root / "native" / "build-app-unix.sh").read_text(
            encoding="utf-8"
        )
        pcre_build = (root / "native" / "pcre2" / "build-unix.sh").read_text(
            encoding="utf-8"
        )
        preflight = (root / "eng" / "preflight.sh").read_text(encoding="utf-8")
        prerequisite_lock = (root / "tests" / "PREREQS.lock").read_text(
            encoding="utf-8"
        )
        gate = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('. "$ROOT/native/toolchain-unix.sh"', native_build)
        self.assertIn('. "$ROOT/native/toolchain-unix.sh"', preflight)
        self.assertLess(
            native_build.index('configure_native_toolchain "$ROOT" "$RID"'),
            native_build.index("build-unix.sh"),
        )
        self.assertIn('    "$CC" "$@" \\\n', pcre_build)
        self.assertIn('ZERO_AR_DATE=1 "$AR" crs', pcre_build)
        self.assertIn('"$RANLIB" "$LIB/libpcre2-8.a"', pcre_build)
        self.assertIn('-isysroot "$SDKROOT"', pcre_build)
        self.assertEqual(6, native_build.count('    "$NATIVE_CC"'))
        self.assertIn('"-fuse-ld=$NATIVE_LD"', native_build)
        self.assertIn('"$NATIVE_STRIP" -x "$path"', native_build)
        self.assertIn('"$NATIVE_NM" -g "$REAL_BIN"', native_build)
        self.assertIn(
            'COMPILER_VERSION="$(native_compiler_version "$NATIVE_CC")"',
            native_build,
        )
        self.assertIn(
            'configure_native_toolchain "$ROOT" "$HOST_RID"',
            preflight,
        )
        self.assertIn(
            'check_file_hash "Apple clang" "$NATIVE_CC"',
            preflight,
        )
        for entry in (
            'macos_host = "macOS 26 arm64"',
            'xcode_version = "26.3"',
            'xcode_build = "17C529"',
            'macos_sdk = "26.2"',
            'macos_deployment_target = "14.0"',
            'apple_clang = "17.0.0 (clang-1700.6.4.2)"',
            'apple_ld = "@(#)PROGRAM:ld PROJECT:ld-1230.1"',
        ):
            self.assertIn(entry, prerequisite_lock)
        for key in (
            "runtime_framework_version",
            "dotnet_host_runtime",
            "compiler_sha256",
            "xcode_version",
            "xcode_build",
            "macos_sdk",
            "macos_deployment_target",
            "linker",
            "linker_sha256",
            "archiver_sha256",
            "ranlib_sha256",
            "strip_sha256",
            "nm_sha256",
        ):
            self.assertIn(f"printf '{key}=%s\\n'", native_build)
            self.assertIn(
                f'read_provenance_value "$SCOUT_BUILD_PROVENANCE" {key}', gate
            )

    @unittest.skipUnless(_BASH, "requires Bash")
    def test_release_driver_rejects_custom_sampling_before_host_setup(self) -> None:
        root = Path(__file__).resolve().parents[2]
        driver = root / "eng" / "run-performance-gate.sh"

        result = subprocess.run(
            [_BASH, str(driver), "--gate", "--runs", "2"],
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
            "RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND=\"$RG_LINE_REGEX_PREFIX "
            "'^[A-Za-z_]{70,90}$' $Q_LINE_REGEX_INPUT\"",
            source,
        )
        self.assertIn(
            "SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND=\"$SCOUT_LINE_REGEX_PREFIX "
            "'^[A-Za-z_]{70,90}$' $Q_LINE_REGEX_INPUT\"",
            source,
        )
        exact_gate = source.index(
            '    "line_regex_bounded_class_exact_general" \\\n'
        )
        next_gate = source.index("\nrun_gate_pair \\\n", exact_gate + 1)
        exact_gate_block = source[exact_gate:next_gate]
        self.assertIn('"$RG_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND"', exact_gate_block)
        self.assertIn(
            '"$SCOUT_LINE_REGEX_BOUNDED_CLASS_EXACT_COMMAND"', exact_gate_block
        )
        self.assertIn('    "1" \\\n', exact_gate_block)
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

    def test_issue_46_nested_literal_gates_remain_in_the_release_suite(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")

        self.assertIn('printf "internal sealed class PaladinRecord\\r\\n"', source)
        self.assertIn('printf "internal sealed class PaladinValue\\r\\n"', source)
        self.assertIn('GATE_NESTED_LITERAL_MATCH_INPUT_COUNT="2"', source)
        self.assertIn('GATE_NESTED_LITERAL_NO_MATCH_INPUT_COUNT="4"', source)
        self.assertIn('"nested_literal_alternation_match_general"', source)
        self.assertIn('"nested_literal_alternation_no_match_general"', source)
        self.assertIn("'(?:Generated|Paladin(?:Record|Value))'", source)
        self.assertIn("'(?:Absent|Missing(?:Two|Three))'", source)
        self.assertIn("$NESTED_LITERAL_MATCH_INPUTS", source)
        self.assertIn("$NESTED_LITERAL_NO_MATCH_INPUTS", source)

        match_gate = source.index(
            '    "nested_literal_alternation_match_general" \\\n'
        )
        no_match_gate = source.index(
            '    "nested_literal_alternation_no_match_general" \\\n'
        )
        next_gate = source.index("\nrun_gate_pair \\\n", no_match_gate + 1)
        match_block = source[match_gate:no_match_gate]
        no_match_block = source[no_match_gate:next_gate]
        self.assertIn('    "0" \\\n', match_block)
        self.assertIn('    "1" \\\n', no_match_block)
        self.assertIn('"$GENERAL_REGEX_ENVIRONMENT"', match_block)
        self.assertIn('"$GENERAL_REGEX_ENVIRONMENT"', no_match_block)

    def test_short_shared_delegate_gate_uses_direct_execution(self) -> None:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        sampler = (root / "bench" / "hyperfine_interleaved.py").read_text(
            encoding="utf-8"
        )

        self.assertIn(
            "run_gate_pair \\\n"
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
        self.assertIn(
            'arguments = [hyperfine, "--style", "none", "--runs", "1", "-N"]',
            sampler,
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

    def test_complete_gate_contract_tracks_root_configuration_inputs(self) -> None:
        root = Path(__file__).resolve().parents[2]
        expected_inputs = (
            ".editorconfig",
            ".gitattributes",
            ".globalconfig",
            "NuGet.Config",
        )
        contracts = (
            (
                root / "eng" / "source-fingerprint.sh",
                'GIT_INDEX_FILE="$temporary_index"',
                "write-tree",
            ),
            (
                root / "eng" / "performance-harness-fingerprint.sh",
                'GIT_INDEX_FILE="$temporary_index"',
                "write-tree",
            ),
            (
                root / "eng" / "run-performance-gate.sh",
                'performance_inputs="$(git',
                'if [ -n "$performance_inputs" ]',
            ),
            (
                root / "bench" / "run-hyperfine.sh",
                "performance_inputs_dirty() {",
                "\nshell_quote() {",
            ),
            (
                root / "native" / "build-app-unix.sh",
                'SOURCE_DIRTY="0"',
                'SOURCE_DIRTY="1"',
            ),
        )

        for path, start_marker, end_marker in contracts:
            source = path.read_text(encoding="utf-8")
            start = source.index(start_marker)
            end = source.index(end_marker, start)
            contract = source[start:end]
            for expected_input in expected_inputs:
                with self.subTest(path=path.name, expected_input=expected_input):
                    self.assertIn(expected_input, contract)

    @unittest.skipUnless(_GIT, "requires Git")
    def test_root_configuration_changes_both_performance_fingerprints(self) -> None:
        root = Path(__file__).resolve().parents[2]
        helpers = (
            root / "eng" / "source-fingerprint.sh",
            root / "eng" / "performance-harness-fingerprint.sh",
        )
        configuration_files = (
            ".editorconfig",
            ".gitattributes",
            ".globalconfig",
            "NuGet.Config",
        )

        with tempfile.TemporaryDirectory() as temporary_directory:
            repository = Path(temporary_directory)
            for directory in (
                repository / ".github" / "workflows",
                repository / "bench",
                repository / "eng",
                repository / "native",
                repository / "src",
                repository / "tests",
            ):
                directory.mkdir(parents=True, exist_ok=True)
            for relative_path in (
                ".github/workflows/release-gates.yml",
                "bench/placeholder",
                "Directory.Build.props",
                "Directory.Build.rsp",
                "Directory.Build.targets",
                "Directory.Packages.props",
                "global.json",
                "native/build-app-unix.sh",
                "native/placeholder",
                "Scout.slnx",
                "src/placeholder",
                "tests/PREREQS.lock",
            ):
                path = repository / relative_path
                path.write_text("baseline\n", encoding="utf-8")
            for configuration_file in configuration_files:
                (repository / configuration_file).write_text(
                    "baseline\n", encoding="utf-8"
                )
            for helper in helpers:
                (repository / "eng" / helper.name).write_bytes(helper.read_bytes())

            subprocess.run(
                [_GIT, "init", "--quiet", str(repository)],
                check=True,
                capture_output=True,
                text=True,
            )

            for helper in helpers:
                copied_helper = repository / "eng" / helper.name
                baseline = subprocess.run(
                    [_SH, str(copied_helper)],
                    check=True,
                    capture_output=True,
                    text=True,
                ).stdout.strip()
                for configuration_file in configuration_files:
                    configuration_path = repository / configuration_file
                    configuration_path.write_text(
                        f"changed {configuration_file}\n", encoding="utf-8"
                    )
                    changed = subprocess.run(
                        [_SH, str(copied_helper)],
                        check=True,
                        capture_output=True,
                        text=True,
                    ).stdout.strip()
                    with self.subTest(
                        helper=helper.name, configuration_file=configuration_file
                    ):
                        self.assertNotEqual(baseline, changed)
                    configuration_path.write_text("baseline\n", encoding="utf-8")

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

        environment = dict(os.environ)
        environment["SCOUT_PERFORMANCE_GATE_INNER"] = "1"
        invalid = subprocess.run(
            [_SH, str(script), "--gate", "--workload", "not-a-workload"],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
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
        result = self._run_gate(scenario="performance", selected="other")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("verify:", result.stdout)
        self.assertNotIn("sample:", result.stdout)
        self.assertNotIn("report:", result.stdout)
        self.assertIn("failures:0:", result.stdout)

    def test_performance_failure_is_recorded_without_resampling(self) -> None:
        result = self._run_gate(scenario="performance")

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertNotIn("retry", result.stdout + result.stderr)
        self.assertEqual(1, result.stdout.count("verify:sample_workload"))
        self.assertEqual(1, result.stdout.count("sample:"))
        self.assertEqual(1, result.stdout.count("report:"))
        self.assertIn("failures:1: sample_workload", result.stdout)

    def test_success_samples_once(self) -> None:
        result = self._run_gate(scenario="pass")

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, result.stdout.count("verify:sample_workload"))
        self.assertEqual(1, result.stdout.count("sample:"))
        self.assertEqual(1, result.stdout.count("report:"))
        self.assertIn(
            "sample:sample_workload|cwd=/tmp|exit=0|env=MODE=general",
            result.stdout,
        )
        self.assertIn("failures:0:", result.stdout)

    def test_performance_failure_does_not_skip_later_workloads(self) -> None:
        result = self._run_gate(scenario="sequence-performance", sequence=True)

        self.assertEqual(1, result.returncode, result.stderr)
        self.assertEqual(3, result.stdout.count("verify:"))
        self.assertEqual(3, result.stdout.count("sample:"))
        self.assertEqual(3, result.stdout.count("report:"))
        self.assertIn("sample:first", result.stdout)
        self.assertIn("sample:second", result.stdout)
        self.assertIn("sample:third", result.stdout)
        self.assertIn("failures:1: second", result.stdout)

    def test_infrastructure_failure_stops_the_gate_immediately(self) -> None:
        result = self._run_gate(scenario="sequence-infrastructure", sequence=True)

        self.assertEqual(1, result.returncode)
        self.assertIn("sample:first", result.stdout)
        self.assertIn("report:first", result.stdout)
        self.assertIn("sample:second", result.stdout)
        self.assertNotIn("report:second", result.stdout)
        self.assertNotIn("sample:third", result.stdout)
        self.assertNotIn("failures:", result.stdout)
        self.assertIn("Hyperfine sampling failed for second.", result.stderr)

    def _run_gate(
        self, scenario: str, selected: str = "", sequence: bool = False
    ) -> subprocess.CompletedProcess[str]:
        root = Path(__file__).resolve().parents[2]
        source = (root / "bench" / "run-hyperfine.sh").read_text(encoding="utf-8")
        start = source.index("run_gate_pair_impl() {")
        end = source.index("\nrun_pair_impl() {", start)
        functions = source[start:end]
        gate_invocation = (
            "run_gate_pair {name} 1.500 'rg {name}' "
            "'scout {name}' 6 6 /tmp 0 'MODE=general'"
        )
        if sequence:
            invocations = "\n".join(
                gate_invocation.format(name=name)
                for name in ("first", "second", "third")
            )
        else:
            invocations = gate_invocation.format(name="sample_workload")
        harness = f"""#!/bin/sh
set -eu
OUT_DIR=/tmp/gate
PYTHON=python_stub
ROOT=/tmp
WORKLOAD={selected!r}
PERFORMANCE_INPUT_MANIFEST=/tmp/performance-inputs.json
PERFORMANCE_REPRO_MANIFEST=/tmp/reproducibility.json
PERFORMANCE_GATE_FAILED_STATUS=10
FAILED_GATE_COUNT=0
FAILED_GATE_WORKLOADS=
scenario={scenario!r}
fail() {{
    printf 'fatal:%s\\n' "$1" >&2
    exit 1
}}
python_stub() {{
    printf 'verify:%s\\n' "$3"
    return 0
}}
workload_selected() {{
    [ -z "$WORKLOAD" ] || [ "$WORKLOAD" = "$1" ]
}}
run_hyperfine_interleaved() {{
    printf 'sample:%s|cwd=%s|exit=%s|env=%s\\n' "$2" "$7" "$8" "$9"
    if [ "$scenario" = sequence-infrastructure ] && [ "$2" = second ]; then
        return 1
    fi
    return 0
}}
report_interleaved_gate() {{
    printf 'report:%s\\n' "$1"
    case "$scenario:$1" in
        performance:*|sequence-performance:second)
            return "$PERFORMANCE_GATE_FAILED_STATUS"
            ;;
    esac
    return 0
}}
{functions}
{invocations}
printf 'failures:%s:%s\\n' "$FAILED_GATE_COUNT" "$FAILED_GATE_WORKLOADS"
[ "$FAILED_GATE_COUNT" -eq 0 ]
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
