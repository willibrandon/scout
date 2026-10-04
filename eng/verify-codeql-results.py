#!/usr/bin/env python3
"""Fail when a CodeQL SARIF scan is missing, unsuccessful, or has any findings."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys


def verify_results(directory: Path) -> int:
    files = sorted(directory.rglob("*.sarif"))
    if not files:
        raise ValueError(f"No CodeQL SARIF files found in {directory}.")

    findings = 0
    for path in files:
        document = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(document, dict) or document.get("version") != "2.1.0":
            raise ValueError(f"{path}: expected a SARIF 2.1.0 document.")
        runs = document.get("runs")
        if not isinstance(runs, list) or not runs:
            raise ValueError(f"{path}: expected at least one SARIF run.")
        for run in runs:
            if not isinstance(run, dict) or not isinstance(run.get("results"), list):
                raise ValueError(f"{path}: missing or invalid results array.")
            for invocation in run.get("invocations", []):
                if invocation.get("executionSuccessful") is False:
                    raise ValueError(f"{path}: CodeQL execution failed.")
                for key in ("toolExecutionNotifications", "toolConfigurationNotifications"):
                    for notification in invocation.get(key, []):
                        if notification.get("level") == "error":
                            raise ValueError(f"{path}: CodeQL reported an error: {notification['message']}")
            for result in run["results"]:
                if not isinstance(result, dict):
                    raise ValueError(f"{path}: invalid finding.")
                findings += 1
                message = result.get("message", {})
                text = message.get("text", message.get("markdown", ""))
                summary = " ".join(text.split())
                if len(summary) > 500:
                    summary = summary[:500] + "… (full message in SARIF artifact)"
                locations = result.get("locations", [])
                location = locations[0].get("physicalLocation", {}) if locations else {}
                source = location.get("artifactLocation", {}).get("uri", str(path))
                line = location.get("region", {}).get("startLine", 1)
                print(f"{source}:{line}: {result.get('ruleId', 'unknown rule')}: {summary}", file=sys.stderr)

    if findings:
        print(f"CodeQL reported {findings} finding(s). Every finding must be resolved.", file=sys.stderr)
        return 1
    print(f"CodeQL reported zero findings across {len(files)} SARIF file(s).")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    try:
        return verify_results(args.directory)
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        print(f"CodeQL verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
