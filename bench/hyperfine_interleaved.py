#!/usr/bin/env python3
"""Run Hyperfine samples in balanced ABBA/BAAB cycles and aggregate them."""

from __future__ import annotations

import argparse
import json
import math
import shlex
import statistics
import subprocess
import sys
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any

from benchmark_environment import (
    add_benchmark_environment_assignments,
    create_benchmark_environment,
)
from write_performance_manifest import read_performance_manifest


_ROUND_ROLES = (
    ("rg", "scout", "scout", "rg"),
    ("scout", "rg", "rg", "scout"),
)
_MAX_TIMER_RESOLUTION_REPLACEMENTS = 8
_REPOSITORY_ROOT = Path(__file__).resolve().parents[1]


def round_roles(round_number: int) -> tuple[str, ...]:
    """Return the command roles for one one-based round in the alternating schedule."""
    if round_number <= 0:
        raise ValueError("round number must be positive")

    return _ROUND_ROLES[(round_number - 1) % len(_ROUND_ROLES)]


def balanced_schedule(rounds: int) -> tuple[str, ...]:
    """Return alternating ABBA/BAAB roles for an even number of rounds."""
    if rounds < 0:
        raise ValueError("rounds must be non-negative")
    if rounds % len(_ROUND_ROLES) != 0:
        raise ValueError("rounds must form complete ABBA/BAAB cycles")

    return tuple(
        role
        for round_number in range(1, rounds + 1)
        for role in round_roles(round_number)
    )


def sample_name(workload: str, round_number: int, position: int, role: str) -> str:
    """Return the expected Hyperfine command name for one balanced sample."""
    return f"{role}:{workload}:round-{round_number}:position-{position}"


def aggregate_round_documents(
    round_documents: Sequence[Mapping[str, Any]],
    workload: str,
    expected_exit_code: int = 0,
) -> dict[str, Any]:
    """Validate and aggregate raw one-run Hyperfine ABBA/BAAB documents."""
    if not round_documents:
        raise ValueError("at least one measured ABBA/BAAB cycle is required")
    if len(round_documents) % len(_ROUND_ROLES) != 0:
        raise ValueError("measured rounds must form complete ABBA/BAAB cycles")

    wall_times = {"rg": [], "scout": []}
    user_times = {"rg": [], "scout": []}
    system_times = {"rg": [], "scout": []}
    exit_codes = {"rg": [], "scout": []}
    memory_samples = {"rg": [], "scout": []}
    round_ratios = []

    for round_number, document in enumerate(round_documents, start=1):
        round_wall_times = {"rg": [], "scout": []}
        for position, (role, result) in enumerate(
            _validated_round_results(document, workload, round_number), start=1
        ):
            wall_time = _wall_time(result, round_number, position, role)
            wall_times[role].append(wall_time)
            round_wall_times[role].append(wall_time)
            user_times[role].append(
                _number(result, "user", round_number, position, non_negative=True)
            )
            system_times[role].append(
                _number(result, "system", round_number, position, non_negative=True)
            )
            exit_codes[role].append(
                _single_exit_code(
                    result,
                    round_number,
                    position,
                    expected_exit_code,
                )
            )

            # Darwin reports RUSAGE_CHILDREN peak RSS cumulatively within one
            # Hyperfine process. Only the leading command is uncontaminated.
            # Alternating rounds provide one clean sample per role in each cycle.
            if position == 1:
                memory_samples[role].append(
                    _single_number(
                        result,
                        "memory_usage_byte",
                        round_number,
                        position,
                        positive=True,
                    )
                )

        log_ratio = (
            math.log(round_wall_times["scout"][0])
            + math.log(round_wall_times["scout"][1])
            - math.log(round_wall_times["rg"][0])
            - math.log(round_wall_times["rg"][1])
        ) / 2
        try:
            round_ratio = math.exp(log_ratio)
        except OverflowError as error:
            raise ValueError(
                f"round {round_number} produced an invalid paired ratio"
            ) from error
        if not math.isfinite(round_ratio) or round_ratio <= 0:
            raise ValueError(
                f"round {round_number} produced an invalid paired ratio"
            )
        round_ratios.append(round_ratio)

    cycle_ratios = []
    for index in range(0, len(round_ratios), len(_ROUND_ROLES)):
        try:
            cycle_ratio = math.exp(
                statistics.fmean(
                    math.log(value)
                    for value in round_ratios[index : index + len(_ROUND_ROLES)]
                )
            )
        except OverflowError as error:
            raise ValueError(
                f"cycle {(index // len(_ROUND_ROLES)) + 1} produced an invalid ratio"
            ) from error
        if not math.isfinite(cycle_ratio) or cycle_ratio <= 0:
            raise ValueError(
                f"cycle {(index // len(_ROUND_ROLES)) + 1} produced an invalid ratio"
            )
        cycle_ratios.append(cycle_ratio)
    expected_time_samples = len(round_documents) * 2
    expected_memory_samples = len(round_documents) // len(_ROUND_ROLES)
    for role in ("rg", "scout"):
        if len(wall_times[role]) != expected_time_samples:
            raise ValueError(f"{role} has an unbalanced timing sample count")
        if len(memory_samples[role]) != expected_memory_samples:
            raise ValueError(f"{role} has an unbalanced RSS sample count")

    return {
        "sampling": {
            "strategy": "ABBA/BAAB",
            "rounds": len(round_documents),
            "cycles": len(cycle_ratios),
            "timing_samples_per_command": expected_time_samples,
            "rss_samples_per_command": expected_memory_samples,
            "round_ratios": round_ratios,
            "cycle_ratios": cycle_ratios,
            "median_ratio": statistics.median(cycle_ratios),
        },
        "results": [
            _aggregate_result(
                f"rg:{workload}",
                wall_times["rg"],
                user_times["rg"],
                system_times["rg"],
                exit_codes["rg"],
                memory_samples["rg"],
            ),
            _aggregate_result(
                f"scout:{workload}",
                wall_times["scout"],
                user_times["scout"],
                system_times["scout"],
                exit_codes["scout"],
                memory_samples["scout"],
            ),
        ],
    }


def _validated_round_results(
    document: Mapping[str, Any], workload: str, round_number: int
) -> tuple[tuple[str, Mapping[str, Any]], ...]:
    results = document.get("results")
    roles = round_roles(round_number)
    if not isinstance(results, list) or len(results) != len(roles):
        raise ValueError(
            f"round {round_number} must contain exactly four balanced results"
        )

    validated = []
    for position, (role, result) in enumerate(zip(roles, results), start=1):
        if not isinstance(result, Mapping):
            raise ValueError(
                f"round {round_number} position {position} is not a result object"
            )

        expected_name = sample_name(workload, round_number, position, role)
        if result.get("command") != expected_name:
            raise ValueError(
                f"round {round_number} position {position} expected "
                f"{expected_name!r}, got {result.get('command')!r}"
            )
        validated.append((role, result))

    return tuple(validated)


def _wall_time(
    result: Mapping[str, Any], round_number: int, position: int, role: str
) -> float:
    wall_time = _single_number(result, "times", round_number, position)
    if wall_time <= 0:
        raise ValueError(
            f"measured round {round_number} position {position} ({role}) "
            f"reported {wall_time:g} seconds"
        )

    return wall_time


def _timer_resolution_error(
    document: Mapping[str, Any],
    workload: str,
    round_number: int,
    expected_exit_code: int,
) -> str | None:
    for position, (role, result) in enumerate(
        _validated_round_results(document, workload, round_number), start=1
    ):
        _single_exit_code(
            result,
            round_number,
            position,
            expected_exit_code,
        )
        wall_time = _single_number(result, "times", round_number, position)
        if wall_time < 0:
            raise ValueError(
                f"measured round {round_number} position {position} ({role}) "
                f"reported an invalid negative wall time: {wall_time:g} seconds"
            )
        if wall_time == 0:
            return (
                f"measured round {round_number} position {position} ({role}) "
                f"reported {wall_time:g} seconds"
            )

    return None


def _single_number(
    result: Mapping[str, Any],
    key: str,
    round_number: int,
    position: int,
    *,
    positive: bool = False,
) -> float:
    value = result.get(key)
    if not isinstance(value, list) or len(value) != 1:
        raise ValueError(
            f"round {round_number} position {position} must contain one {key} value"
        )

    return _validated_number(
        value[0], key, round_number, position, positive=positive
    )


def _number(
    result: Mapping[str, Any],
    key: str,
    round_number: int,
    position: int,
    *,
    non_negative: bool = False,
) -> float:
    return _validated_number(
        result.get(key),
        key,
        round_number,
        position,
        non_negative=non_negative,
    )


def _validated_number(
    value: Any,
    key: str,
    round_number: int,
    position: int,
    *,
    positive: bool = False,
    non_negative: bool = False,
) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(
            f"round {round_number} position {position} has invalid {key} value"
        )

    number = float(value)
    if not math.isfinite(number):
        raise ValueError(
            f"round {round_number} position {position} has non-finite {key} value"
        )
    if positive and number <= 0:
        raise ValueError(
            f"round {round_number} position {position} has non-positive {key} value"
        )
    if non_negative and number < 0:
        raise ValueError(
            f"round {round_number} position {position} has negative {key} value"
        )

    return number


def _single_exit_code(
    result: Mapping[str, Any],
    round_number: int,
    position: int,
    expected_exit_code: int,
) -> int:
    value = result.get("exit_codes")
    if (
        not isinstance(value, list)
        or len(value) != 1
        or isinstance(value[0], bool)
        or not isinstance(value[0], int)
    ):
        raise ValueError(
            f"round {round_number} position {position} has an invalid exit code"
        )
    if value[0] != expected_exit_code:
        raise ValueError(
            f"round {round_number} position {position} expected exit "
            f"{expected_exit_code}, got {value[0]}"
        )

    return value[0]


def _aggregate_result(
    command: str,
    wall_times: Sequence[float],
    user_times: Sequence[float],
    system_times: Sequence[float],
    exit_codes: Sequence[int],
    memory_samples: Sequence[float],
) -> dict[str, Any]:
    return {
        "command": command,
        "mean": statistics.fmean(wall_times),
        "stddev": statistics.stdev(wall_times) if len(wall_times) > 1 else None,
        "median": statistics.median(wall_times),
        "user": statistics.fmean(user_times),
        "system": statistics.fmean(system_times),
        "min": min(wall_times),
        "max": max(wall_times),
        "times": list(wall_times),
        "exit_codes": list(exit_codes),
        "user_times": list(user_times),
        "system_times": list(system_times),
        "memory_usage_byte": [round(value) for value in memory_samples],
    }


def _run_balanced_round(
    hyperfine: str,
    workload: str,
    round_number: int,
    rg_command: str,
    scout_command: str,
    expected_exit_code: int,
    working_directory: Path,
    output: Path | None,
    environment: Mapping[str, str],
) -> None:
    arguments = [hyperfine, "--style", "none", "--runs", "1", "-N"]
    if expected_exit_code != 0:
        arguments.append(f"--ignore-failure={expected_exit_code}")
    if output is not None:
        output.unlink(missing_ok=True)
        arguments.extend(("--export-json", str(output)))

    commands = {"rg": rg_command, "scout": scout_command}
    for position, role in enumerate(round_roles(round_number), start=1):
        arguments.extend(
            (
                "--command-name",
                sample_name(workload, round_number, position, role),
                commands[role],
            )
        )

    subprocess.run(
        arguments,
        check=True,
        cwd=working_directory,
        env=environment,
    )


def _read_document(path: Path) -> Mapping[str, Any]:
    with path.open(encoding="utf-8") as stream:
        document = json.load(stream)
    if not isinstance(document, Mapping):
        raise ValueError(f"{path} does not contain a JSON object")

    return document


def _write_document(path: Path, document: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, indent=2)
        stream.write("\n")


def _geometric_mean(values: Sequence[float]) -> float:
    return math.exp(statistics.fmean(math.log(value) for value in values))


def _print_cycle_summaries(document: Mapping[str, Any]) -> None:
    sampling = document["sampling"]
    results = document["results"]
    rg = results[0]
    scout = results[1]
    cycles = sampling["cycles"]
    print("  cycle    rg (s)  Scout (s)   ratio")
    for cycle_index in range(cycles):
        offset = cycle_index * 4
        rg_seconds = _geometric_mean(rg["times"][offset : offset + 4])
        scout_seconds = _geometric_mean(
            scout["times"][offset : offset + 4]
        )
        print(
            f"  {cycle_index + 1:>5}  {rg_seconds:>8.3f}  "
            f"{scout_seconds:>9.3f}  "
            f"{sampling['cycle_ratios'][cycle_index]:>7.3f}x"
        )


def run_interleaved(args: argparse.Namespace) -> None:
    """Run balanced warmup and measured cycles, then write aggregate JSON."""
    output = Path(args.output)
    output.unlink(missing_ok=True)
    environment = add_benchmark_environment_assignments(
        create_benchmark_environment(output), args.environment
    )
    working_directory = Path(args.working_directory).resolve(strict=True)
    if not working_directory.is_dir():
        raise ValueError(
            f"working directory is not a directory: {working_directory}"
        )
    performance_input_manifest = Path(
        args.performance_input_manifest
    ).resolve(strict=True)
    performance_inputs = _read_document(performance_input_manifest).get(
        "inputs"
    )
    if not isinstance(performance_inputs, list) or not performance_inputs:
        raise ValueError(
            "generated performance-input manifest must contain a nonempty "
            "inputs array"
        )
    reproducibility = read_performance_manifest(
        Path(args.reproducibility_manifest)
    )
    sample_directory = output.with_name(f"{output.stem}.samples")
    sample_directory.mkdir(parents=True, exist_ok=True)
    for stale_round in sample_directory.glob("round-*.json"):
        stale_round.unlink()

    print(
        f"Sampling target: {args.warmup_rounds} warm-up + "
        f"{args.rounds} valid measured "
        "rounds (alternating ABBA/BAAB)",
        flush=True,
    )
    for warmup_round in range(1, args.warmup_rounds + 1):
        _run_balanced_round(
            args.hyperfine,
            f"{args.name}:warmup",
            warmup_round,
            args.rg_command,
            args.scout_command,
            args.expected_exit_code,
            working_directory,
            output=None,
            environment=environment,
        )

    raw_paths = []
    discarded_paths = []
    round_documents = []
    for round_number in range(1, args.rounds + 1):
        raw_path = sample_directory / f"round-{round_number}.json"
        while True:
            _run_balanced_round(
                args.hyperfine,
                args.name,
                round_number,
                args.rg_command,
                args.scout_command,
                args.expected_exit_code,
                working_directory,
                output=raw_path,
                environment=environment,
            )
            round_document = _read_document(raw_path)
            timer_resolution_error = _timer_resolution_error(
                round_document,
                args.name,
                round_number,
                args.expected_exit_code,
            )
            if timer_resolution_error is not None:
                discarded_number = len(discarded_paths) + 1
                if discarded_number > _MAX_TIMER_RESOLUTION_REPLACEMENTS:
                    failure_path = sample_directory / (
                        f"round-{round_number}.timer-resolution-failure.json"
                    )
                    raw_path.replace(failure_path)
                    raise ValueError(
                        f"{timer_resolution_error}; unable to collect "
                        f"{args.rounds} valid measured rounds after "
                        f"{_MAX_TIMER_RESOLUTION_REPLACEMENTS} timer-resolution "
                        f"replacements; final invalid sample saved to {failure_path}"
                    )

                discarded_path = sample_directory / (
                    f"round-{round_number}.discarded-{discarded_number}.json"
                )
                raw_path.replace(discarded_path)
                discarded_paths.append(str(discarded_path))
                replacements = len(discarded_paths)
                print(
                    "  Timer-resolution sample discarded: "
                    f"{timer_resolution_error}; replacing the entire balanced round "
                    f"({replacements}/{_MAX_TIMER_RESOLUTION_REPLACEMENTS}).",
                    flush=True,
                )
                continue
            break

        raw_paths.append(str(raw_path))
        round_documents.append(round_document)

    document = aggregate_round_documents(
        round_documents,
        args.name,
        args.expected_exit_code,
    )
    document["commands"] = {
        "rg": args.rg_command,
        "scout": args.scout_command,
    }
    document["command_argv"] = {
        "rg": shlex.split(args.rg_command),
        "scout": shlex.split(args.scout_command),
    }
    document["execution_mode"] = "direct"
    document["expected_exit_code"] = args.expected_exit_code
    document["performance_inputs"] = performance_inputs
    document["reproducibility"] = reproducibility
    document["environment"] = dict(environment)
    document["working_directory"] = str(working_directory)
    document["sampling"]["raw_round_files"] = raw_paths
    document["sampling"]["warmup_rounds"] = args.warmup_rounds
    document["sampling"]["measured_round_attempts"] = (
        args.rounds + len(discarded_paths)
    )
    document["sampling"]["timer_resolution_replacements"] = len(
        discarded_paths
    )
    document["sampling"]["discarded_round_files"] = discarded_paths
    _write_document(output, document)
    if discarded_paths:
        noun = "round" if len(discarded_paths) == 1 else "rounds"
        print(
            f"Collected {args.rounds} valid measured rounds after replacing "
            f"{len(discarded_paths)} invalid timer-resolution {noun}.",
            flush=True,
        )
    _print_cycle_summaries(document)


def _positive_integer(value: str) -> int:
    number = int(value)
    if number <= 0:
        raise argparse.ArgumentTypeError("must be a positive integer")
    return number


def _positive_even_integer(value: str) -> int:
    number = _positive_integer(value)
    if number % len(_ROUND_ROLES) != 0:
        raise argparse.ArgumentTypeError(
            "must form complete ABBA/BAAB cycles (use an even number)"
        )
    return number


def _non_negative_integer(value: str) -> int:
    number = int(value)
    if number < 0:
        raise argparse.ArgumentTypeError("must be a non-negative integer")
    return number


def _non_negative_even_integer(value: str) -> int:
    number = _non_negative_integer(value)
    if number % len(_ROUND_ROLES) != 0:
        raise argparse.ArgumentTypeError(
            "must form complete ABBA/BAAB cycles (use zero or an even number)"
        )
    return number


def _exit_code(value: str) -> int:
    number = int(value)
    if number < 0 or number > 255:
        raise argparse.ArgumentTypeError("must be between 0 and 255")
    return number


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run balanced ABBA/BAAB Hyperfine samples for a Scout gate workload."
        )
    )
    parser.add_argument("--hyperfine", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--rg-command", required=True)
    parser.add_argument("--scout-command", required=True)
    parser.add_argument("--rounds", required=True, type=_positive_even_integer)
    parser.add_argument(
        "--warmup-rounds", required=True, type=_non_negative_even_integer
    )
    parser.add_argument("--output", required=True)
    parser.add_argument("--working-directory", required=True, type=Path)
    parser.add_argument(
        "--performance-input-manifest", required=True, type=Path
    )
    parser.add_argument(
        "--reproducibility-manifest", required=True, type=Path
    )
    parser.add_argument(
        "--expected-exit-code", required=True, type=_exit_code
    )
    parser.add_argument("--environment", action="append", default=[])
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    try:
        run_interleaved(_parse_args(sys.argv[1:] if argv is None else argv))
    except (OSError, ValueError, json.JSONDecodeError, subprocess.CalledProcessError) as error:
        print(f"balanced Hyperfine sampling failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
