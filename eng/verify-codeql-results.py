#!/usr/bin/env python3
"""Require successful CodeQL scans and resolve findings in Scout-owned code."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import posixpath
import sys
from urllib.parse import unquote, urlsplit


def is_third_party(result: dict, roots: list[str]) -> bool:
    locations = result.get("locations", [])
    if not roots or not locations:
        return False
    for location in locations:
        artifact = location.get("physicalLocation", {}).get("artifactLocation", {})
        uri = artifact.get("uri")
        if not isinstance(uri, str):
            return False
        parsed = urlsplit(uri)
        if parsed.scheme or parsed.netloc:
            return False
        source = posixpath.normpath(unquote(parsed.path).replace("\\", "/"))
        if not any(source.startswith(root + "/") for root in roots):
            return False
    return True


def reviewed_reason(result: dict, policy: dict | None, repository: Path) -> str | None:
    if policy is None:
        return None
    locations = result.get("locations", [])
    if len(locations) != 1:
        return None
    artifact = locations[0].get("physicalLocation", {}).get("artifactLocation", {})
    uri = artifact.get("uri")
    if not isinstance(uri, str):
        return None
    parsed = urlsplit(uri)
    source = unquote(parsed.path).replace("\\", "/")
    if (parsed.scheme or parsed.netloc or source.startswith("/")
            or posixpath.normpath(source) != source or source.startswith("../")):
        return None
    native = policy["required_native_interop"]
    if result.get("ruleId") in native["rules"] and source in native["sources"]:
        return native["reason"]
    for review in policy["false_positives"]:
        if (result.get("ruleId") == review["rule"] and source == review["source"]
                and result.get("message", {}).get("text") == review["message"]):
            contents = (repository / source).read_bytes()
            if hashlib.sha256(contents).hexdigest() == review["sha256"]:
                return review["reason"]
    return None


def verify_results(directory: Path, third_party_roots: list[str], policy: dict | None, repository: Path) -> int:
    files = sorted(directory.rglob("*.sarif"))
    if not files:
        raise ValueError(f"No CodeQL SARIF files found in {directory}.")

    findings = 0
    third_party_findings = 0
    reviewed_findings = 0
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
                third_party = is_third_party(result, third_party_roots)
                reason = reviewed_reason(result, policy, repository)
                if third_party:
                    third_party_findings += 1
                elif reason:
                    reviewed_findings += 1
                else:
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
                ownership = "third-party source (reported unchanged): " if third_party else ""
                if reason:
                    ownership = "reviewed report (kept visible): "
                print(f"{source}:{line}: {ownership}{result.get('ruleId', 'unknown rule')}: {summary}", file=sys.stderr)
                if reason:
                    print(f"  {reason}", file=sys.stderr)

    if third_party_findings:
        print(f"CodeQL reported {third_party_findings} finding(s) in unchanged third-party source. "
              "All findings remain in the uploaded SARIF and GitHub code scanning.", file=sys.stderr)
    if reviewed_findings:
        print(f"CodeQL reported {reviewed_findings} reviewed report(s). "
              "All reports remain in the uploaded SARIF and GitHub code scanning.", file=sys.stderr)

    if findings:
        print(f"CodeQL reported {findings} finding(s) in Scout-owned code. Every applicable finding must be resolved.", file=sys.stderr)
        return 1
    print(f"CodeQL reported zero findings requiring changes in Scout-owned code across {len(files)} SARIF file(s).")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--third-party-root", action="append", default=[],
                        help="Report all findings in this unchanged vendored source directory without failing the Scout gate.")
    parser.add_argument("--reviewed-reports", type=Path,
                        help="Report the explicitly reviewed, inapplicable C# findings without failing CI.")
    args = parser.parse_args()
    try:
        for root in args.third_party_root:
            if (not root or root.startswith("/") or posixpath.normpath(root) != root or "\\" in root
                    or root in (".", "..") or root.startswith("../")):
                raise ValueError(f"Invalid third-party source directory: {root!r}.")
        policy = None if args.reviewed_reports is None else json.loads(args.reviewed_reports.read_text(encoding="utf-8"))
        return verify_results(args.directory, args.third_party_root, policy, Path(__file__).resolve().parent.parent)
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        print(f"CodeQL verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
