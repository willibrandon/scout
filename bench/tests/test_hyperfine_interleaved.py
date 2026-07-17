"""Deterministic tests for balanced Hyperfine gate sampling."""

from __future__ import annotations

import argparse
import io
import json
import math
import statistics
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hyperfine_interleaved import (
    _parse_args,
    _run_balanced_round,
    aggregate_round_documents,
    balanced_schedule,
    round_roles,
    run_interleaved,
    sample_name,
)


class HyperfineInterleavedTests(unittest.TestCase):
    """Verify balanced sampling and strict Hyperfine result validation."""

    def test_six_round_schedule_balances_roles_in_every_position(self) -> None:
        schedule = balanced_schedule(6)

        self.assertEqual(
            (
                "rg",
                "scout",
                "scout",
                "rg",
                "scout",
                "rg",
                "rg",
                "scout",
            )
            * 3,
            schedule,
        )
        for position in range(4):
            roles = schedule[position::4]
            self.assertEqual(3, roles.count("rg"))
            self.assertEqual(3, roles.count("scout"))

        with self.assertRaises(ValueError):
            balanced_schedule(5)

    def test_even_round_invocation_uses_baab_order_and_no_shell_mode(self) -> None:
        with patch("hyperfine_interleaved.subprocess.run") as run:
            _run_balanced_round(
                "hyperfine",
                "workload",
                2,
                "rg command",
                "scout command",
                no_shell=True,
                output=None,
            )

        arguments = run.call_args.args[0]
        self.assertEqual(
            [
                "hyperfine",
                "--style",
                "none",
                "--runs",
                "1",
                "-N",
                "--command-name",
                "scout:workload:round-2:position-1",
                "scout command",
                "--command-name",
                "rg:workload:round-2:position-2",
                "rg command",
                "--command-name",
                "rg:workload:round-2:position-3",
                "rg command",
                "--command-name",
                "scout:workload:round-2:position-4",
                "scout command",
            ],
            arguments,
        )
        run.assert_called_once_with(arguments, check=True)

    def test_command_line_requires_complete_measured_and_warmup_cycles(self) -> None:
        arguments = [
            "--hyperfine",
            "hyperfine",
            "--name",
            "workload",
            "--rg-command",
            "rg command",
            "--scout-command",
            "scout command",
            "--rounds",
            "2",
            "--warmup-rounds",
            "0",
            "--output",
            "result.json",
        ]

        parsed = _parse_args(arguments)
        self.assertEqual(2, parsed.rounds)
        self.assertEqual(0, parsed.warmup_rounds)

        for option, index in (("--rounds", 9), ("--warmup-rounds", 11)):
            with self.subTest(option=option):
                invalid = list(arguments)
                invalid[index] = "1"
                with redirect_stderr(io.StringIO()):
                    with self.assertRaises(SystemExit):
                        _parse_args(invalid)

    def test_run_removes_stale_round_documents_before_sampling(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "result.json"
            sample_directory = Path(temporary_directory) / "result.samples"
            sample_directory.mkdir()
            stale_round = sample_directory / "round-99.json"
            unrelated = sample_directory / "notes.txt"
            stale_round.write_text("stale", encoding="utf-8")
            unrelated.write_text("keep", encoding="utf-8")
            arguments = argparse.Namespace(
                output=str(output),
                warmup_rounds=0,
                rounds=2,
                hyperfine="hyperfine",
                name="workload",
                rg_command="rg command",
                scout_command="scout command",
                no_shell=False,
            )

            with redirect_stdout(io.StringIO()), patch(
                "hyperfine_interleaved._run_balanced_round",
                side_effect=RuntimeError("stop after cleanup"),
            ):
                with self.assertRaises(RuntimeError):
                    run_interleaved(arguments)

            self.assertFalse(stale_round.exists())
            self.assertTrue(unrelated.exists())

    def test_run_prints_one_compact_summary_without_per_round_chatter(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "result.json"
            arguments = argparse.Namespace(
                output=str(output),
                warmup_rounds=2,
                rounds=2,
                hyperfine="hyperfine",
                name="workload",
                rg_command="rg command",
                scout_command="scout command",
                no_shell=False,
            )

            def write_round_document(
                _hyperfine: str,
                workload: str,
                round_number: int,
                _rg_command: str,
                _scout_command: str,
                _no_shell: bool,
                output: Path | None,
            ) -> None:
                if output is not None:
                    output.write_text(
                        json.dumps(self._round_document(workload, round_number)),
                        encoding="utf-8",
                    )

            standard_output = io.StringIO()
            with redirect_stdout(standard_output), patch(
                "hyperfine_interleaved._run_balanced_round",
                side_effect=write_round_document,
            ) as run_round:
                run_interleaved(arguments)

            self.assertEqual(4, run_round.call_count)
            self.assertEqual(
                "Sampling target: 2 warm-up + 2 valid measured rounds "
                "(alternating ABBA/BAAB)\n"
                "  cycle    rg (s)  Scout (s)   ratio\n"
                "      1    16.708     16.745    1.002x\n",
                standard_output.getvalue(),
            )
            self.assertNotIn("warmup round 1/2", standard_output.getvalue())
            self.assertNotIn("measured round 1/2", standard_output.getvalue())

    def test_run_replaces_timer_resolution_sample_and_keeps_valid_round_target(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "result.json"
            arguments = argparse.Namespace(
                output=str(output),
                warmup_rounds=2,
                rounds=2,
                hyperfine="hyperfine",
                name="workload",
                rg_command="rg command",
                scout_command="scout command",
                no_shell=False,
            )
            measured_attempts = 0

            def write_round_document(
                _hyperfine: str,
                workload: str,
                round_number: int,
                _rg_command: str,
                _scout_command: str,
                _no_shell: bool,
                output: Path | None,
            ) -> None:
                nonlocal measured_attempts
                if output is None:
                    return

                measured_attempts += 1
                document = self._round_document(workload, round_number)
                if measured_attempts == 1:
                    document["results"][3]["times"] = [0.0]
                output.write_text(json.dumps(document), encoding="utf-8")

            standard_output = io.StringIO()
            with redirect_stdout(standard_output), patch(
                "hyperfine_interleaved._run_balanced_round",
                side_effect=write_round_document,
            ) as run_round:
                run_interleaved(arguments)

            aggregate = json.loads(output.read_text(encoding="utf-8"))
            sampling = aggregate["sampling"]
            self.assertEqual(
                {"rg": "rg command", "scout": "scout command"},
                aggregate["commands"],
            )
            self.assertEqual(5, run_round.call_count)
            self.assertEqual(2, sampling["rounds"])
            self.assertEqual(3, sampling["measured_round_attempts"])
            self.assertEqual(1, sampling["timer_resolution_replacements"])
            self.assertEqual(2, len(sampling["raw_round_files"]))
            self.assertEqual(1, len(sampling["discarded_round_files"]))
            self.assertEqual(4, len(aggregate["results"][0]["times"]))
            self.assertEqual(4, len(aggregate["results"][1]["times"]))
            self.assertTrue(Path(sampling["discarded_round_files"][0]).exists())
            self.assertIn(
                "Timer-resolution sample discarded: measured round 1 position 4 "
                "(rg) reported 0 seconds; replacing the entire balanced round "
                "(1/8).",
                standard_output.getvalue(),
            )
            self.assertIn(
                "Collected 2 valid measured rounds after replacing 1 invalid "
                "timer-resolution round.",
                standard_output.getvalue(),
            )

    def test_run_stops_after_bounded_timer_resolution_replacements(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "result.json"
            arguments = argparse.Namespace(
                output=str(output),
                warmup_rounds=0,
                rounds=2,
                hyperfine="hyperfine",
                name="workload",
                rg_command="rg command",
                scout_command="scout command",
                no_shell=False,
            )

            def write_invalid_round(
                _hyperfine: str,
                workload: str,
                round_number: int,
                _rg_command: str,
                _scout_command: str,
                _no_shell: bool,
                output: Path | None,
            ) -> None:
                document = self._round_document(workload, round_number)
                document["results"][0]["times"] = [0.0]
                if output is None:
                    raise AssertionError("measured fixture requires an output path")
                output.write_text(json.dumps(document), encoding="utf-8")

            standard_output = io.StringIO()
            with redirect_stdout(standard_output), patch(
                "hyperfine_interleaved._MAX_TIMER_RESOLUTION_REPLACEMENTS", 2
            ), patch(
                "hyperfine_interleaved._run_balanced_round",
                side_effect=write_invalid_round,
            ) as run_round:
                with self.assertRaisesRegex(
                    ValueError,
                    "unable to collect 2 valid measured rounds after 2 "
                    "timer-resolution replacements",
                ):
                    run_interleaved(arguments)

            self.assertEqual(3, run_round.call_count)
            self.assertEqual(
                2,
                standard_output.getvalue().count(
                    "replacing the entire balanced round"
                ),
            )
            sample_directory = output.with_name("result.samples")
            self.assertTrue(
                (sample_directory / "round-1.timer-resolution-failure.json").exists()
            )
            self.assertFalse(output.exists())

    def test_run_rejects_negative_timing_without_replacement(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "result.json"
            arguments = argparse.Namespace(
                output=str(output),
                warmup_rounds=0,
                rounds=2,
                hyperfine="hyperfine",
                name="workload",
                rg_command="rg command",
                scout_command="scout command",
                no_shell=False,
            )

            def write_invalid_round(
                _hyperfine: str,
                workload: str,
                round_number: int,
                _rg_command: str,
                _scout_command: str,
                _no_shell: bool,
                output: Path | None,
            ) -> None:
                document = self._round_document(workload, round_number)
                document["results"][0]["times"] = [-0.001]
                if output is None:
                    raise AssertionError("measured fixture requires an output path")
                output.write_text(json.dumps(document), encoding="utf-8")

            standard_output = io.StringIO()
            with redirect_stdout(standard_output), patch(
                "hyperfine_interleaved._run_balanced_round",
                side_effect=write_invalid_round,
            ) as run_round:
                with self.assertRaisesRegex(
                    ValueError,
                    "reported an invalid negative wall time",
                ):
                    run_interleaved(arguments)

            self.assertEqual(1, run_round.call_count)
            self.assertNotIn(
                "replacing the entire balanced round",
                standard_output.getvalue(),
            )
            self.assertFalse(output.exists())

    def test_balanced_cycles_resist_drift_without_masking_regression(self) -> None:
        drift = [math.log(position + 1) for position in range(1, 25)]
        grouped_rg_positions = (*range(1, 7), *range(19, 25))
        grouped_scout_positions = tuple(range(7, 19))

        grouped_ratio = self._drifted_ratio(
            drift, grouped_rg_positions, grouped_scout_positions, 1.20
        )
        balanced = aggregate_round_documents(
            self._drift_documents("workload", drift, 1.20), "workload"
        )
        regressed = aggregate_round_documents(
            self._drift_documents("workload", drift, 1.30), "workload"
        )
        exponential_regression = aggregate_round_documents(
            self._drift_documents(
                "workload",
                [1.3**position for position in range(1, 25)],
                1.30,
            ),
            "workload",
        )

        self.assertGreater(grouped_ratio, 1.25)
        self.assertLess(balanced["sampling"]["median_ratio"], 1.25)
        self.assertGreater(regressed["sampling"]["median_ratio"], 1.25)
        self.assertAlmostEqual(
            1.30, exponential_regression["sampling"]["median_ratio"], places=12
        )

    def test_balanced_cycles_cancel_first_position_cache_cost(self) -> None:
        first_position_cold = [
            2.0 if (position - 1) % 4 == 0 else 1.0
            for position in range(1, 25)
        ]

        aggregate = aggregate_round_documents(
            self._drift_documents("workload", first_position_cold, 1.20),
            "workload",
        )

        self.assertAlmostEqual(1.20, aggregate["sampling"]["median_ratio"], places=12)

    def test_aggregation_preserves_balanced_time_cpu_and_rss_samples(self) -> None:
        documents = [
            self._round_document("workload", round_number)
            for round_number in range(1, 7)
        ]

        aggregate = aggregate_round_documents(documents, "workload")

        self.assertEqual("ABBA/BAAB", aggregate["sampling"]["strategy"])
        self.assertEqual(3, aggregate["sampling"]["cycles"])
        self.assertEqual(12, aggregate["sampling"]["timing_samples_per_command"])
        self.assertEqual(3, aggregate["sampling"]["rss_samples_per_command"])
        self.assertEqual(6, len(aggregate["sampling"]["round_ratios"]))
        self.assertEqual(3, len(aggregate["sampling"]["cycle_ratios"]))
        self.assertEqual(12, len(aggregate["results"][0]["times"]))
        self.assertEqual(12, len(aggregate["results"][1]["times"]))
        self.assertEqual(3, len(aggregate["results"][0]["memory_usage_byte"]))
        self.assertEqual(3, len(aggregate["results"][1]["memory_usage_byte"]))
        self.assertEqual(
            [10_000_001] * 3, aggregate["results"][0]["memory_usage_byte"]
        )
        self.assertEqual(
            [10_000_001] * 3, aggregate["results"][1]["memory_usage_byte"]
        )

    def test_aggregation_rejects_missing_unbalanced_and_nonpositive_samples(self) -> None:
        missing = self._round_document("workload", 1)
        missing["results"].pop()

        unbalanced = self._round_document("workload", 1)
        unbalanced["results"][3]["command"] = sample_name(
            "workload", 1, 4, "scout"
        )

        nonpositive = self._round_document("workload", 1)
        nonpositive["results"][0]["times"] = [0.0]

        for documents in (
            [missing, self._round_document("workload", 2)],
            [unbalanced, self._round_document("workload", 2)],
            [nonpositive, self._round_document("workload", 2)],
            [self._round_document("workload", 1)],
        ):
            with self.subTest(documents=documents):
                with self.assertRaises(ValueError):
                    aggregate_round_documents(documents, "workload")

    @staticmethod
    def _drifted_ratio(
        drift: list[float],
        rg_positions: tuple[int, ...],
        scout_positions: tuple[int, ...],
        scout_ratio: float,
    ) -> float:
        rg_median = statistics.median(drift[position - 1] for position in rg_positions)
        scout_median = statistics.median(
            scout_ratio * drift[position - 1] for position in scout_positions
        )
        return scout_median / rg_median

    @staticmethod
    def _round_document(workload: str, round_number: int) -> dict[str, object]:
        results = []
        for position, role in enumerate(round_roles(round_number), start=1):
            results.append(
                {
                    "command": sample_name(workload, round_number, position, role),
                    "times": [float(round_number * 10 + position)],
                    "user": float(position) / 10,
                    "system": float(position) / 20,
                    "exit_codes": [0],
                    "memory_usage_byte": [10_000_000 + position],
                }
            )
        return {"results": results}

    @staticmethod
    def _drift_documents(
        workload: str, drift: list[float], scout_ratio: float
    ) -> list[dict[str, object]]:
        if len(drift) != 24:
            raise ValueError("the drift fixture requires three ABBA/BAAB cycles")

        documents = []
        for round_index in range(6):
            results = []
            for position, role in enumerate(
                round_roles(round_index + 1), start=1
            ):
                drift_index = (round_index * 4) + position - 1
                role_ratio = scout_ratio if role == "scout" else 1.0
                results.append(
                    {
                        "command": sample_name(
                            workload, round_index + 1, position, role
                        ),
                        "times": [drift[drift_index] * role_ratio],
                        "user": 0.1,
                        "system": 0.1,
                        "exit_codes": [0],
                        "memory_usage_byte": [10_000_000],
                    }
                )
            documents.append({"results": results})

        return documents


if __name__ == "__main__":
    unittest.main()
