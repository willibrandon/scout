"""Deterministic tests for the Hyperfine ABBA sampling helper."""

from __future__ import annotations

import math
import statistics
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hyperfine_interleaved import (
    _run_abba_round,
    abba_schedule,
    aggregate_round_documents,
    sample_name,
)


class HyperfineInterleavedTests(unittest.TestCase):
    """Verify balanced sampling and strict Hyperfine result validation."""

    def test_five_round_schedule_is_balanced_abba(self) -> None:
        schedule = abba_schedule(5)

        self.assertEqual(("rg", "scout", "scout", "rg") * 5, schedule)
        self.assertEqual(10, schedule.count("rg"))
        self.assertEqual(10, schedule.count("scout"))

    def test_round_invocation_uses_exact_abba_order_and_no_shell_mode(self) -> None:
        with patch("hyperfine_interleaved.subprocess.run") as run:
            _run_abba_round(
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
                "rg:workload:round-2:position-1",
                "rg command",
                "--command-name",
                "scout:workload:round-2:position-2",
                "scout command",
                "--command-name",
                "scout:workload:round-2:position-3",
                "scout command",
                "--command-name",
                "rg:workload:round-2:position-4",
                "rg command",
            ],
            arguments,
        )
        run.assert_called_once_with(arguments, check=True)

    def test_abba_resists_monotonic_drift_without_masking_regression(self) -> None:
        drift = [math.log(position + 1) for position in range(1, 21)]
        grouped_rg_positions = (1, 2, 3, 4, 5, 16, 17, 18, 19, 20)
        grouped_scout_positions = tuple(range(6, 16))

        grouped_ratio = self._drifted_ratio(
            drift, grouped_rg_positions, grouped_scout_positions, 1.20
        )
        abba = aggregate_round_documents(
            self._drift_documents("workload", drift, 1.20), "workload"
        )
        regressed_abba = aggregate_round_documents(
            self._drift_documents("workload", drift, 1.30), "workload"
        )
        exponential_regression = aggregate_round_documents(
            self._drift_documents(
                "workload",
                [1.3**position for position in range(1, 21)],
                1.30,
            ),
            "workload",
        )

        self.assertGreater(grouped_ratio, 1.25)
        self.assertLess(abba["sampling"]["median_ratio"], 1.25)
        self.assertAlmostEqual(
            1.2053249575958502, abba["sampling"]["median_ratio"], places=12
        )
        self.assertGreater(regressed_abba["sampling"]["median_ratio"], 1.25)
        self.assertAlmostEqual(
            1.30, exponential_regression["sampling"]["median_ratio"], places=12
        )

    def test_aggregation_preserves_balanced_time_cpu_and_rss_samples(self) -> None:
        documents = [
            self._round_document("workload", round_number)
            for round_number in range(1, 6)
        ]

        aggregate = aggregate_round_documents(documents, "workload")

        self.assertEqual("ABBA", aggregate["sampling"]["strategy"])
        self.assertEqual(10, aggregate["sampling"]["timing_samples_per_command"])
        self.assertEqual(5, aggregate["sampling"]["rss_samples_per_command"])
        self.assertEqual(5, len(aggregate["sampling"]["round_ratios"]))
        self.assertEqual(10, len(aggregate["results"][0]["times"]))
        self.assertEqual(10, len(aggregate["results"][1]["times"]))
        self.assertEqual(5, len(aggregate["results"][0]["memory_usage_byte"]))
        self.assertEqual(5, len(aggregate["results"][1]["memory_usage_byte"]))
        self.assertEqual(
            [10_000_001] * 5, aggregate["results"][0]["memory_usage_byte"]
        )
        self.assertEqual(
            [10_000_002] * 5, aggregate["results"][1]["memory_usage_byte"]
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

        for document in (missing, unbalanced, nonpositive):
            with self.subTest(document=document):
                with self.assertRaises(ValueError):
                    aggregate_round_documents([document], "workload")

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
        for position, role in enumerate(("rg", "scout", "scout", "rg"), start=1):
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
        if len(drift) != 20:
            raise ValueError("the drift fixture requires five ABBA rounds")

        documents = []
        for round_index in range(5):
            results = []
            for position, role in enumerate(
                ("rg", "scout", "scout", "rg"), start=1
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
