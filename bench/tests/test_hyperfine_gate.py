"""Deterministic tests for aggregate Hyperfine gate diagnostics."""

from __future__ import annotations

import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hyperfine_gate import evaluate_gate, format_gate_report, gate_exit_code, main


class HyperfineGateTests(unittest.TestCase):
    """Verify exact gate evaluation and unambiguous attempt reporting."""

    def test_rss_failure_exposes_hidden_one_decimal_difference(self) -> None:
        evaluation = evaluate_gate(
            self._document(
                wall_ratio=1.025,
                rg_memory=4_423_680,
                scout_memory=17_072_128,
            ),
            wall_limit=1.5,
            scout_floor_bytes=10_420_224,
        )

        report = format_gate_report(evaluation, "line_regex_word_boundary_general")

        self.assertEqual(("RSS",), evaluation["failures"])
        self.assertEqual(-65_536, evaluation["rss_margin_four"])
        self.assertIn("1.025000x within 1.500000x limit", report)
        self.assertIn(
            "CPU   1.000000x (Scout median 0.300000s; "
            "rg 0.300000s; diagnostic only)",
            report,
        )
        self.assertIn("17072128 bytes (16.281 MiB)", report)
        self.assertIn("17055744 bytes (16.266 MiB)", report)
        self.assertIn("excess +16384 bytes (+0.016 MiB)", report)
        self.assertIn("median of 3 clean samples", report)
        self.assertIn(
            "Result: FAIL (line_regex_word_boundary_general: RSS)", report
        )
        self.assertNotIn("PASS", report)
        self.assertEqual(11, gate_exit_code(evaluation))

    def test_passing_attempt_reports_headroom_and_one_overall_result(self) -> None:
        evaluation = evaluate_gate(
            self._document(
                wall_ratio=1.25,
                rg_memory=8_388_608,
                scout_memory=20_971_520,
            ),
            wall_limit=1.5,
            scout_floor_bytes=10_485_760,
        )

        report = format_gate_report(evaluation, "bounded_assignment_no_match")

        self.assertEqual((), evaluation["failures"])
        self.assertIn("wall  1.250000x within 1.500000x limit", report)
        self.assertIn("headroom +2097152 bytes (+2.000 MiB)", report)
        self.assertIn("Result: PASS (bounded_assignment_no_match)", report)
        self.assertEqual(1, report.count("PASS"))
        self.assertNotIn("FAIL", report)
        self.assertEqual(0, gate_exit_code(evaluation))

    def test_final_failure_names_both_dimensions_once(self) -> None:
        evaluation = evaluate_gate(
            self._document(
                wall_ratio=1.75,
                rg_memory=4_194_304,
                scout_memory=17_825_792,
            ),
            wall_limit=1.5,
            scout_floor_bytes=10_485_760,
        )

        report = format_gate_report(
            evaluation,
            "line_regex_word_boundary_general",
            "after the initial attempt and 2 retries",
        )

        self.assertEqual(("wall", "RSS"), evaluation["failures"])
        self.assertIn("1.750000x exceeded 1.500000x limit", report)
        self.assertIn(
            "Result: FAIL after the initial attempt and 2 retries "
            "(line_regex_word_boundary_general: wall and RSS)",
            report,
        )
        self.assertEqual(1, report.count("FAIL"))
        self.assertNotIn("PASS", report)
        self.assertEqual(12, gate_exit_code(evaluation))

    def test_final_failure_uses_singular_retry_context(self) -> None:
        evaluation = evaluate_gate(
            self._document(1.75, 8_388_608, 20_971_520),
            wall_limit=1.5,
            scout_floor_bytes=10_485_760,
        )

        report = format_gate_report(
            evaluation,
            "cold_version",
            "after the initial attempt and 1 retry",
        )

        self.assertIn(
            "Result: FAIL after the initial attempt and 1 retry "
            "(cold_version: wall)",
            report,
        )

    def test_exact_rss_limit_is_within_gate(self) -> None:
        evaluation = evaluate_gate(
            self._document(
                wall_ratio=1.5,
                rg_memory=4_194_304,
                scout_memory=16_777_216,
            ),
            wall_limit=1.5,
            scout_floor_bytes=10_485_760,
        )

        report = format_gate_report(evaluation, "exact_limit")

        self.assertTrue(evaluation["wall_within"])
        self.assertTrue(evaluation["rss_within"])
        self.assertIn("headroom +0 bytes (+0.000 MiB)", report)
        self.assertIn("Result: PASS", report)

    def test_even_rss_sample_medians_preserve_half_and_quarter_bytes(self) -> None:
        document = self._document(
            wall_ratio=1.0,
            rg_memory=1,
            scout_memory=3,
        )
        document["sampling"]["rss_samples_per_command"] = 2
        document["results"][0]["memory_usage_byte"] = [1, 2]
        document["results"][1]["memory_usage_byte"] = [3, 4]

        evaluation = evaluate_gate(
            document,
            wall_limit=1.5,
            scout_floor_bytes=1,
        )
        report = format_gate_report(evaluation, "even_rss_samples")

        self.assertEqual(3, evaluation["rg_rss_twice"])
        self.assertEqual(7, evaluation["scout_rss_twice"])
        self.assertEqual(13, evaluation["rss_limit_four"])
        self.assertEqual(-1, evaluation["rss_margin_four"])
        self.assertIn("Scout median 3.5 bytes", report)
        self.assertIn("limit 3.25 bytes", report)
        self.assertIn("excess +0.25 bytes", report)
        self.assertIn("Result: FAIL (even_rss_samples: RSS)", report)

    def test_rejects_missing_or_multiline_workload_name(self) -> None:
        evaluation = evaluate_gate(
            self._document(1.0, 4_194_304, 10_485_760),
            wall_limit=1.5,
            scout_floor_bytes=10_485_760,
        )

        with self.assertRaisesRegex(ValueError, "non-empty single line"):
            format_gate_report(evaluation, " ")
        with self.assertRaisesRegex(ValueError, "non-empty single line"):
            format_gate_report(evaluation, "first\nsecond")

    def test_cli_returns_dimension_exit_code_and_rejects_invalid_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            input_path = Path(temporary_directory) / "aggregate.json"
            input_path.write_text(
                json.dumps(
                    self._document(
                        wall_ratio=1.75,
                        rg_memory=8_388_608,
                        scout_memory=20_971_520,
                    )
                ),
                encoding="utf-8",
            )
            standard_output = io.StringIO()
            with redirect_stdout(standard_output):
                exit_code = main(
                    (
                        "--input",
                        str(input_path),
                        "--wall-limit",
                        "1.5",
                        "--scout-rss-floor",
                        "10485760",
                        "--workload",
                        "line_regex_word_boundary_general",
                    )
                )

            self.assertEqual(10, exit_code)
            self.assertIn(
                "Result: FAIL (line_regex_word_boundary_general: wall)",
                standard_output.getvalue(),
            )

            input_path.write_text("{}", encoding="utf-8")
            standard_error = io.StringIO()
            with redirect_stderr(standard_error):
                exit_code = main(
                    (
                        "--input",
                        str(input_path),
                        "--wall-limit",
                        "1.5",
                        "--scout-rss-floor",
                        "10485760",
                        "--workload",
                        "line_regex_word_boundary_general",
                    )
                )

            self.assertEqual(2, exit_code)
            self.assertIn("missing sampling data", standard_error.getvalue())

    def test_rejects_malformed_or_unbalanced_rss_samples(self) -> None:
        missing_sampling = self._document(1.0, 4_194_304, 10_485_760)
        missing_sampling.pop("sampling")
        with self.assertRaisesRegex(ValueError, "missing sampling data"):
            evaluate_gate(missing_sampling, 1.5, 10_485_760)

        unbalanced = self._document(1.0, 4_194_304, 10_485_760)
        unbalanced["results"][1]["memory_usage_byte"].pop()
        with self.assertRaisesRegex(ValueError, "different RSS sample counts"):
            evaluate_gate(unbalanced, 1.5, 10_485_760)

        invalid = self._document(1.0, 4_194_304, 10_485_760)
        invalid["results"][0]["memory_usage_byte"][0] = 0
        with self.assertRaisesRegex(ValueError, "invalid RSS sample"):
            evaluate_gate(invalid, 1.5, 10_485_760)

    def test_cpu_ratio_uses_median_combined_user_and_system_samples(self) -> None:
        document = self._document(1.0, 4_194_304, 10_485_760)
        document["results"][0]["user_times"] = [1.0, 2.0, 9.0]
        document["results"][0]["system_times"] = [0.5, 0.5, 0.5]
        document["results"][1]["user_times"] = [2.0, 5.0, 20.0]
        document["results"][1]["system_times"] = [1.0, 1.0, 1.0]

        evaluation = evaluate_gate(document, 1.5, 10_485_760)
        report = format_gate_report(evaluation, "capture")

        self.assertEqual(
            {"rg_seconds": 2.5, "scout_seconds": 6.0, "ratio": 2.4},
            evaluation["cpu_summary"],
        )
        self.assertIn(
            "CPU   2.400000x (Scout median 6.000000s; "
            "rg 2.500000s; diagnostic only)",
            report,
        )

    def test_cpu_diagnostic_does_not_change_gate_outcome(self) -> None:
        document = self._document(1.0, 4_194_304, 10_485_760)
        document["results"][0]["user_times"] = [0.1, 0.1, 0.1]
        document["results"][1]["user_times"] = [10.0, 10.0, 10.0]

        evaluation = evaluate_gate(document, 1.5, 10_485_760)

        self.assertGreater(evaluation["cpu_summary"]["ratio"], 20.0)
        self.assertEqual((), evaluation["failures"])
        self.assertEqual(0, gate_exit_code(evaluation))

    def test_rejects_incomplete_cpu_samples(self) -> None:
        document = self._document(1.0, 4_194_304, 10_485_760)
        document["results"][1].pop("system_times")

        with self.assertRaisesRegex(ValueError, "CPU samples are incomplete"):
            evaluate_gate(document, 1.5, 10_485_760)

    @staticmethod
    def _document(
        wall_ratio: float, rg_memory: int, scout_memory: int
    ) -> dict[str, object]:
        return {
            "sampling": {
                "median_ratio": wall_ratio,
                "rss_samples_per_command": 3,
            },
            "results": [
                {
                    "command": "rg:workload",
                    "memory_usage_byte": [rg_memory] * 3,
                    "user_times": [0.2] * 3,
                    "system_times": [0.1] * 3,
                },
                {
                    "command": "scout:workload",
                    "memory_usage_byte": [scout_memory] * 3,
                    "user_times": [0.2] * 3,
                    "system_times": [0.1] * 3,
                },
            ],
        }
