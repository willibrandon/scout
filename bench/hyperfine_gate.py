#!/usr/bin/env python3
"""Evaluate and report one aggregate Hyperfine release-gate attempt."""

from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any


_MIB = 1024 * 1024
_WALL_FAILURE_EXIT_CODE = 10
_RSS_FAILURE_EXIT_CODE = 11
_BOTH_FAILURE_EXIT_CODE = 12


def evaluate_gate(
    document: Mapping[str, Any], wall_limit: float, scout_floor_bytes: int
) -> dict[str, Any]:
    """Evaluate exact wall-time and peak-RSS gates from one aggregate document."""
    wall_limit = _positive_number(wall_limit, "wall limit")
    if isinstance(scout_floor_bytes, bool) or not isinstance(scout_floor_bytes, int):
        raise ValueError("Scout RSS floor must be an integer byte count")
    if scout_floor_bytes <= 0:
        raise ValueError("Scout RSS floor must be positive")

    sampling = document.get("sampling")
    if not isinstance(sampling, Mapping):
        raise ValueError("aggregate document is missing sampling data")
    wall_ratio = _positive_number(sampling.get("median_ratio"), "median ratio")

    results = document.get("results")
    if not isinstance(results, list):
        raise ValueError("aggregate document is missing results")
    rg_result = _result_for_role(results, "rg")
    scout_result = _result_for_role(results, "scout")
    cpu_summary = _optional_cpu_summary(rg_result, scout_result)
    rg_memory = _memory_samples(rg_result, "rg")
    scout_memory = _memory_samples(scout_result, "Scout")
    if len(rg_memory) != len(scout_memory):
        raise ValueError("rg and Scout have different RSS sample counts")

    expected_rss_samples = sampling.get("rss_samples_per_command")
    if (
        isinstance(expected_rss_samples, bool)
        or not isinstance(expected_rss_samples, int)
        or expected_rss_samples <= 0
    ):
        raise ValueError("aggregate document has an invalid RSS sample count")
    if len(rg_memory) != expected_rss_samples:
        raise ValueError("aggregate RSS sample count does not match sampling metadata")

    rg_rss_twice = _median_twice(rg_memory)
    scout_rss_twice = _median_twice(scout_memory)

    # A median of an even number of integer byte samples can be a half byte,
    # and multiplying that median by 1.5 can produce a quarter-byte limit.
    # Quarter-byte integer arithmetic therefore preserves the gate exactly.
    rss_limit_four = (3 * rg_rss_twice) + (4 * scout_floor_bytes)
    rss_margin_four = rss_limit_four - (2 * scout_rss_twice)
    wall_within = wall_ratio <= wall_limit
    rss_within = rss_margin_four >= 0
    failures = tuple(
        dimension
        for dimension, within in (("wall", wall_within), ("RSS", rss_within))
        if not within
    )

    return {
        "wall_ratio": wall_ratio,
        "wall_limit": wall_limit,
        "wall_within": wall_within,
        "cpu_summary": cpu_summary,
        "rg_rss_twice": rg_rss_twice,
        "scout_rss_twice": scout_rss_twice,
        "scout_floor_bytes": scout_floor_bytes,
        "rss_limit_four": rss_limit_four,
        "rss_margin_four": rss_margin_four,
        "rss_within": rss_within,
        "rss_sample_count": expected_rss_samples,
        "failures": failures,
    }


def format_gate_report(
    evaluation: Mapping[str, Any],
    workload: str,
    final_failure_context: str | None = None,
) -> str:
    """Format precise component diagnostics and one overall attempt result."""
    workload = workload.strip()
    if not workload or "\n" in workload or "\r" in workload:
        raise ValueError("workload name must be a non-empty single line")

    wall_ratio = float(evaluation["wall_ratio"])
    wall_limit = float(evaluation["wall_limit"])
    wall_within = bool(evaluation["wall_within"])
    wall_difference = abs(wall_limit - wall_ratio)
    wall_relation = "within" if wall_within else "exceeded"
    wall_margin = "headroom" if wall_within else "excess"

    scout_rss_four = 2 * int(evaluation["scout_rss_twice"])
    rg_rss_four = 2 * int(evaluation["rg_rss_twice"])
    floor_four = 4 * int(evaluation["scout_floor_bytes"])
    limit_four = int(evaluation["rss_limit_four"])
    rss_margin_four = int(evaluation["rss_margin_four"])
    rss_within = bool(evaluation["rss_within"])
    rss_relation = "within" if rss_within else "exceeded"
    rss_margin_name = "headroom" if rss_within else "excess"
    absolute_rss_margin_four = abs(rss_margin_four)
    rss_sample_count = int(evaluation["rss_sample_count"])

    lines = [
        (
            f"  wall  {wall_ratio:.6f}x {wall_relation} "
            f"{wall_limit:.6f}x limit; {wall_margin} +{wall_difference:.6f}x"
        ),
    ]

    cpu_summary = evaluation.get("cpu_summary")
    if isinstance(cpu_summary, Mapping):
        lines.append(
            f"  CPU   {float(cpu_summary['ratio']):.6f}x "
            f"(Scout median {float(cpu_summary['scout_seconds']):.6f}s; "
            f"rg {float(cpu_summary['rg_seconds']):.6f}s; diagnostic only)"
        )

    lines.extend(
        [
            (
                f"  RSS   Scout median {_format_quarter_bytes(scout_rss_four)} bytes "
                f"({_quarters_to_mib(scout_rss_four):.3f} MiB) {rss_relation} limit "
                f"{_format_quarter_bytes(limit_four)} bytes "
                f"({_quarters_to_mib(limit_four):.3f} MiB)"
            ),
            (
                f"        {rss_margin_name} "
                f"+{_format_quarter_bytes(absolute_rss_margin_four)} bytes "
                f"(+{_quarters_to_mib(absolute_rss_margin_four):.3f} MiB); "
                f"median of {rss_sample_count} clean samples"
            ),
            (
                f"        limit = 1.500x rg {_format_quarter_bytes(rg_rss_four)} "
                f"bytes ({_quarters_to_mib(rg_rss_four):.3f} MiB) + Scout floor "
                f"{_format_quarter_bytes(floor_four)} bytes "
                f"({_quarters_to_mib(floor_four):.3f} MiB)"
            ),
        ]
    )

    failures = tuple(evaluation["failures"])
    if not failures:
        lines.append(f"  Result: PASS ({workload})")
    else:
        context = f" {final_failure_context.strip()}" if final_failure_context else ""
        lines.append(
            f"  Result: FAIL{context} ({workload}: {_format_dimensions(failures)})"
        )

    return "\n".join(lines)


def gate_exit_code(evaluation: Mapping[str, Any]) -> int:
    """Return a distinct exit code for the failed gate dimensions."""
    failures = tuple(evaluation["failures"])
    if not failures:
        return 0
    if failures == ("wall",):
        return _WALL_FAILURE_EXIT_CODE
    if failures == ("RSS",):
        return _RSS_FAILURE_EXIT_CODE
    if failures == ("wall", "RSS"):
        return _BOTH_FAILURE_EXIT_CODE

    raise ValueError("evaluation contains unknown failure dimensions")


def _result_for_role(results: Sequence[Any], role: str) -> Mapping[str, Any]:
    matches = [
        result
        for result in results
        if isinstance(result, Mapping)
        and isinstance(result.get("command"), str)
        and result["command"].startswith(f"{role}:")
    ]
    if len(matches) != 1:
        raise ValueError(f"aggregate document must contain one {role} result")

    return matches[0]


def _memory_samples(result: Mapping[str, Any], role: str) -> list[int]:
    values = result.get("memory_usage_byte")
    if not isinstance(values, list) or not values:
        raise ValueError(f"{role} result has no RSS samples")

    samples = []
    for value in values:
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise ValueError(f"{role} result has an invalid RSS sample")
        samples.append(value)

    return samples


def _optional_cpu_summary(
    rg_result: Mapping[str, Any], scout_result: Mapping[str, Any]
) -> dict[str, float] | None:
    results = (("rg", rg_result), ("Scout", scout_result))
    availability = [
        ("user_times" in result, "system_times" in result)
        for _, result in results
    ]
    if all(not user and not system for user, system in availability):
        return None
    if any(not user or not system for user, system in availability):
        raise ValueError("aggregate CPU samples are incomplete")

    medians: dict[str, float] = {}
    for role, result in results:
        user_samples = _cpu_samples(result, "user_times", role)
        system_samples = _cpu_samples(result, "system_times", role)
        if len(user_samples) != len(system_samples):
            raise ValueError(f"{role} user and system CPU sample counts differ")
        medians[role] = statistics.median(
            user + system
            for user, system in zip(user_samples, system_samples, strict=True)
        )

    rg_seconds = medians["rg"]
    scout_seconds = medians["Scout"]
    if rg_seconds <= 0:
        return None

    return {
        "rg_seconds": rg_seconds,
        "scout_seconds": scout_seconds,
        "ratio": scout_seconds / rg_seconds,
    }


def _cpu_samples(
    result: Mapping[str, Any], key: str, role: str
) -> list[float]:
    values = result.get(key)
    if not isinstance(values, list) or not values:
        raise ValueError(f"{role} result has no {key.replace('_', ' ')}")

    samples = []
    for value in values:
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise ValueError(f"{role} result has an invalid CPU sample")
        number = float(value)
        if not math.isfinite(number) or number < 0:
            raise ValueError(f"{role} result has an invalid CPU sample")
        samples.append(number)

    return samples


def _positive_number(value: Any, name: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be numeric")
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise ValueError(f"{name} must be positive and finite")

    return number


def _median_twice(values: Sequence[int]) -> int:
    ordered = sorted(values)
    midpoint = len(ordered) // 2
    if len(ordered) % 2:
        return 2 * ordered[midpoint]

    return ordered[midpoint - 1] + ordered[midpoint]


def _format_quarter_bytes(quarter_bytes: int) -> str:
    whole, fraction = divmod(quarter_bytes, 4)
    suffix = ("", ".25", ".5", ".75")[fraction]
    return f"{whole}{suffix}"


def _quarters_to_mib(quarter_bytes: int) -> float:
    return quarter_bytes / (4 * _MIB)


def _format_dimensions(failures: Sequence[str]) -> str:
    return " and ".join(failures)


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Report one aggregate Scout Hyperfine gate attempt."
    )
    parser.add_argument("--input", required=True)
    parser.add_argument("--wall-limit", required=True, type=float)
    parser.add_argument("--scout-rss-floor", required=True, type=int)
    parser.add_argument("--workload", required=True)
    parser.add_argument("--final-failure-context")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the gate-reporting command-line entry point."""
    arguments = _parse_args(sys.argv[1:] if argv is None else argv)
    try:
        with Path(arguments.input).open(encoding="utf-8") as stream:
            document = json.load(stream)
        if not isinstance(document, Mapping):
            raise ValueError("aggregate input must contain a JSON object")
        evaluation = evaluate_gate(
            document, arguments.wall_limit, arguments.scout_rss_floor
        )
        print(
            format_gate_report(
                evaluation,
                arguments.workload,
                arguments.final_failure_context,
            )
        )
        return gate_exit_code(evaluation)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"Hyperfine gate reporting failed: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
