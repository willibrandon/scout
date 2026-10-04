#!/usr/bin/env python3
"""Parse generated completions and exercise representative shell completion behavior."""

from __future__ import annotations

import argparse
import base64
import gzip
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def main() -> None:
    root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scout", type=Path, required=True)
    parser.add_argument("--shell", action="append", choices=("bash", "zsh", "fish", "powershell"))
    args = parser.parse_args()
    executable = args.scout.resolve()
    shells = args.shell or ["bash", "zsh", "fish", "powershell"]
    results = []
    with tempfile.TemporaryDirectory(prefix="scout-completion-") as directory:
        temporary = Path(directory)
        binary = temporary / "bin"
        binary.mkdir()
        if os.name == "nt":
            (binary / "scout.cmd").write_text(f'@echo off\n"{executable}" %*\n')
        else:
            (binary / "scout").symlink_to(executable)
        environment = os.environ.copy()
        environment["PATH"] = str(binary) + os.pathsep + environment["PATH"]
        for name in ("SCOUT_CONFIG_PATH", "RIPGREP_CONFIG_PATH"):
            environment.pop(name, None)

        def run(command: list[str]) -> str:
            result = subprocess.run(command, cwd=temporary, env=environment, text=True, capture_output=True)
            if result.returncode:
                raise RuntimeError(f"{command[0]} failed:\n{result.stdout}\n{result.stderr}")
            return result.stdout

        for shell in shells:
            program = "pwsh" if shell == "powershell" else shell
            if shutil.which(program) is None:
                raise RuntimeError(f"Missing shell requested for validation: {program}")
            artifact = root / "src/Scout.App/GeneratedArtifacts" / f"complete-{shell}.base64"
            completion = temporary / ("scout.ps1" if shell == "powershell" else f"scout.{shell}")
            completion.write_bytes(gzip.decompress(base64.b64decode(artifact.read_bytes())))
            environment["SCOUT_COMPLETION"] = str(completion)
            if shell == "bash":
                run([program, "-n", str(completion)])
                output = run([program, "-c", 'source "$SCOUT_COMPLETION"; COMP_WORDS=(scout --ig); COMP_CWORD=1; _scout; printf "%s\\n" "${COMPREPLY[@]}"'])
                if "--ignore-case" not in output.splitlines():
                    raise RuntimeError("Bash option completion failed")
            elif shell == "fish":
                run([program, "--no-execute", str(completion)])
                output = run([program, "-c", 'source "$SCOUT_COMPLETION"; complete -C "scout --ig"'])
                if not any(line.startswith("--ignore-case") for line in output.splitlines()):
                    raise RuntimeError("Fish option completion failed")
                output = run([program, "-c", 'source "$SCOUT_COMPLETION"; complete -C "scout --type mo"'])
                if not any(line.startswith("mojo") for line in output.splitlines()):
                    raise RuntimeError("Fish release file-type completion failed")
            elif shell == "powershell":
                script = temporary / "verify.ps1"
                script.write_text('''$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($env:SCOUT_COMPLETION, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
. $env:SCOUT_COMPLETION
$matches = [System.Management.Automation.CommandCompletion]::CompleteInput('scout --ig', 10, $null).CompletionMatches
if ('--ignore-case' -notin $matches.CompletionText) { throw 'PowerShell option completion failed' }
''')
                run([program, "-NoProfile", "-File", str(script)])
            else:
                run([program, "-n", str(completion)])
                # Run the release's actual option-inventory test with Scout's transformed function.
                test_source = root / "upstream/ripgrep-e89fff89/ci/test-complete"
                script = temporary / "ci/test-complete"
                script.parent.mkdir()
                script.write_text(test_source.read_text().replace("_RG_COMPLETE_LIST_ARGS", "_SCOUT_COMPLETE_LIST_ARGS"))
                reference = temporary / "target/release/rg"
                reference.parent.mkdir(parents=True)
                reference.symlink_to(executable)
                function = temporary / "crates/core/flags/complete/rg.zsh"
                function.parent.mkdir(parents=True)
                shutil.copyfile(completion, function)
                run([program, str(script)])
            results.append({"shell": shell, "parsed": True, "behavior": True})
            print(f"OK {shell} completion", flush=True)
    output = root / "artifacts" / "completion-tests"
    output.mkdir(parents=True, exist_ok=True)
    (output / "results.json").write_text(json.dumps(results, indent=2) + "\n")


if __name__ == "__main__":
    main()
