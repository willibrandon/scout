#!/usr/bin/env python3
"""Require successful measurements for every release interval and traversal benchmark."""

import argparse
import json
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifacts", type=Path)
    args = parser.parse_args()
    expected = {"UpstreamIntervalCompilationBenchmarks": 9, "UpstreamTraversalBenchmarks": 4}
    for name, count in expected.items():
        report = args.artifacts / "results" / f"Scout.{name}-report-full-compressed.json"
        benchmarks = json.loads(report.read_text())["Benchmarks"]
        if len(benchmarks) != count or any(not item.get("Statistics") for item in benchmarks):
            raise RuntimeError(f"Incomplete or failed benchmark measurements: {name}")
        print(f"OK {name}: {count} measurements")


if __name__ == "__main__":
    main()
