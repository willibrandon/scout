#!/usr/bin/env python3
"""Run Hyperfine samples in balanced ABBA rounds and aggregate their results."""

from __future__ import annotations

import argparse
import json
import math
import statistics
import subprocess
import sys
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any


_ABBA_ROLES = ("rg", "scout", "scout", "rg")


def abba_schedule(rounds: int) -> tuple[str, ...]:
    """Return the balanced command roles for the requested number of rounds."""
    if rounds < 0:
        raise ValueError("rounds must be non-negative")

    return _ABBA_ROLES * rounds


def sample_name(workload: str, round_number: int, position: int, role: str) -> str:
    """Return the expected Hyperfine command name for one ABBA sample."""
    return f"{role}:{workload}:round-{round_number}:position-{position}"


def aggregate_round_documents(
    round_documents: Sequence[Mapping[str, Any]], workload: str
) -> dict[str, Any]:
    """Validate and aggregate raw one-run Hyperfine ABBA documents."""
    if not round_documents:
        raise ValueError("at least one measured ABBA round is required")

    wall_times = {"rg": [], "scout": []}
    user_times = {"rg": [], "scout": []}
    system_times = {"rg": [], "scout": []}
    exit_codes = {"rg": [], "scout": []}
    memory_samples = {"rg": [], "scout": []}
    round_ratios = []

    for round_number, document in enumerate(round_documents, start=1):
        results = document.get("results")
        if not isinstance(results, list) or len(results) != len(_ABBA_ROLES):
            raise ValueError(
                f"round {round_number} must contain exactly four ABBA results"
            )

        round_wall_times = {"rg": [], "scout": []}
        for position, (role, result) in enumerate(
            zip(_ABBA_ROLES, results, strict=True), start=1
        ):
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

            wall_time = _single_number(
                result, "times", round_number, position, positive=True
            )
            wall_times[role].append(wall_time)
            round_wall_times[role].append(wall_time)
            user_times[role].append(
                _number(result, "user", round_number, position, non_negative=True)
            )
            system_times[role].append(
                _number(result, "system", round_number, position, non_negative=True)
            )
            exit_codes[role].append(
                _single_exit_code(result, round_number, position)
            )

            # Darwin reports RUSAGE_CHILDREN peak RSS cumulatively within one
            # Hyperfine process. Only the leading rg/Scout pair is uncontaminated
            # by a preceding Scout process. A fresh process is used for each round.
            if position <= 2:
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

    expected_time_samples = len(round_documents) * 2
    expected_memory_samples = len(round_documents)
    for role in ("rg", "scout"):
        if len(wall_times[role]) != expected_time_samples:
            raise ValueError(f"{role} has an unbalanced timing sample count")
        if len(memory_samples[role]) != expected_memory_samples:
            raise ValueError(f"{role} has an unbalanced RSS sample count")

    return {
        "sampling": {
            "strategy": "ABBA",
            "rounds": len(round_documents),
            "timing_samples_per_command": expected_time_samples,
            "rss_samples_per_command": expected_memory_samples,
            "round_ratios": round_ratios,
            "median_ratio": statistics.median(round_ratios),
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
    result: Mapping[str, Any], round_number: int, position: int
) -> int:
    value = result.get("exit_codes")
    if (
        not isinstance(value, list)
        or len(value) != 1
        or isinstance(value[0], bool)
        or not isinstance(value[0], int)
        or value[0] != 0
    ):
        raise ValueError(
            f"round {round_number} position {position} did not exit successfully"
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


def _run_abba_round(
    hyperfine: str,
    workload: str,
    round_number: int,
    rg_command: str,
    scout_command: str,
    no_shell: bool,
    output: Path | None,
) -> None:
    arguments = [hyperfine, "--style", "none", "--runs", "1"]
    if no_shell:
        arguments.append("-N")
    if output is not None:
        output.unlink(missing_ok=True)
        arguments.extend(("--export-json", str(output)))

    commands = {"rg": rg_command, "scout": scout_command}
    for position, role in enumerate(abba_schedule(1), start=1):
        arguments.extend(
            (
                "--command-name",
                sample_name(workload, round_number, position, role),
                commands[role],
            )
        )

    subprocess.run(arguments, check=True)


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


def _print_round_summaries(document: Mapping[str, Any], workload: str) -> None:
    sampling = document["sampling"]
    results = document["results"]
    rg = results[0]
    scout = results[1]
    rounds = sampling["rounds"]
    for round_index in range(rounds):
        offset = round_index * 2
        print(
            f"{workload} round {round_index + 1} ABBA seconds: "
            f"rg={rg['times'][offset]:.6f}/{rg['times'][offset + 1]:.6f} "
            f"scout={scout['times'][offset]:.6f}/{scout['times'][offset + 1]:.6f}; "
            f"user rg={rg['user_times'][offset]:.6f}/{rg['user_times'][offset + 1]:.6f} "
            f"scout={scout['user_times'][offset]:.6f}/{scout['user_times'][offset + 1]:.6f}; "
            f"system rg={rg['system_times'][offset]:.6f}/{rg['system_times'][offset + 1]:.6f} "
            f"scout={scout['system_times'][offset]:.6f}/{scout['system_times'][offset + 1]:.6f}; "
            f"rss rg={rg['memory_usage_byte'][round_index]} "
            f"scout={scout['memory_usage_byte'][round_index]} bytes; "
            f"paired ratio={sampling['round_ratios'][round_index]:.6f}x"
        )


def run_interleaved(args: argparse.Namespace) -> None:
    """Run warmup and measured ABBA rounds, then write their aggregate JSON."""
    output = Path(args.output)
    output.unlink(missing_ok=True)
    sample_directory = output.with_name(f"{output.stem}.samples")
    sample_directory.mkdir(parents=True, exist_ok=True)

    for warmup_round in range(1, args.warmup_rounds + 1):
        print(
            f"{args.name} warmup round {warmup_round}/{args.warmup_rounds} ABBA"
        )
        _run_abba_round(
            args.hyperfine,
            f"{args.name}:warmup",
            warmup_round,
            args.rg_command,
            args.scout_command,
            args.no_shell,
            output=None,
        )

    raw_paths = []
    round_documents = []
    for round_number in range(1, args.rounds + 1):
        print(f"{args.name} measured round {round_number}/{args.rounds} ABBA")
        raw_path = sample_directory / f"round-{round_number}.json"
        _run_abba_round(
            args.hyperfine,
            args.name,
            round_number,
            args.rg_command,
            args.scout_command,
            args.no_shell,
            output=raw_path,
        )
        raw_paths.append(str(raw_path))
        round_documents.append(_read_document(raw_path))

    document = aggregate_round_documents(round_documents, args.name)
    document["sampling"]["raw_round_files"] = raw_paths
    _write_document(output, document)
    _print_round_summaries(document, args.name)


def _positive_integer(value: str) -> int:
    number = int(value)
    if number <= 0:
        raise argparse.ArgumentTypeError("must be a positive integer")
    return number


def _non_negative_integer(value: str) -> int:
    number = int(value)
    if number < 0:
        raise argparse.ArgumentTypeError("must be a non-negative integer")
    return number


def _parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run balanced ABBA Hyperfine samples for a Scout gate workload."
    )
    parser.add_argument("--hyperfine", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--rg-command", required=True)
    parser.add_argument("--scout-command", required=True)
    parser.add_argument("--rounds", required=True, type=_positive_integer)
    parser.add_argument(
        "--warmup-rounds", required=True, type=_non_negative_integer
    )
    parser.add_argument("--output", required=True)
    parser.add_argument("--no-shell", action="store_true")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    try:
        run_interleaved(_parse_args(sys.argv[1:] if argv is None else argv))
    except (OSError, ValueError, json.JSONDecodeError, subprocess.CalledProcessError) as error:
        print(f"hyperfine ABBA sampling failed: {error}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
