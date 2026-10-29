"""Tests for the Phase 07 evaluation harness. Run from the project root:

    python -m unittest discover -s scripts/evaluation/tests
"""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import evaluate  # noqa: E402
from sentinel_eval.packages import PackageError, load_package, load_packages  # noqa: E402
from sentinel_eval.records import Run, latest_per_scenario, load_v1, parse_time  # noqa: E402
from sentinel_eval.report import aggregate, gate  # noqa: E402
from sentinel_eval.scoring import classify_observation, score_baseline, score_sentinel, sentinel_label  # noqa: E402

PACKAGES = HERE.parents[2] / "reference-system" / "scenarios" / "packages"
PRICING = {"models": {"meta/muse-spark": {"inputPerMillion": 1.0, "outputPerMillion": 4.0}}, "default": {"inputPerMillion": 0, "outputPerMillion": 0}}
DEPLOY = "deploy-controller:deployment:d1:started"


def evidence(evidence_id: str, hypothesis: str, observations: list[str], status: str = "accepted") -> dict:
    return {"finding": {"evidenceId": evidence_id, "statement": "A signal.", "direction": "supports", "sourceObservationIds": observations, "hypothesisIds": [hypothesis]}, "status": status}


def incident(*, leader_service: str, key: str, change_observation: str | None = DEPLOY, changed_service: str = "checkout-service", version: str = "v2.4.1",
             evidence_items: list[dict] | None = None, outcome: str = "diagnosed", plan: tuple[str, str] | None = ("rollback", "checkout-service"),
             status: str = "awaitingApproval", started: bool = False, missing: list[str] | None = None) -> dict:
    detail = {
        "summary": {"incidentId": "i1", "status": status, "openedAt": "2026-10-28T09:01:00.1234567+00:00"},
        "changes": [{"deploymentId": "d1", "serviceId": changed_service, "version": version, "observationIds": [DEPLOY, "deploy-controller:deployment:d1:completed"]}],
        "evidence": evidence_items if evidence_items is not None else [
            evidence("e1", "h1", ["prometheus:redis_connections:checkout-service:100", "prometheus:redis_connections:checkout-service:160"]),
            evidence("e2", "h1", [DEPLOY]),
        ],
        "hypotheses": [{"finding": {"hypothesisId": "h1", "key": key, "suspectedServiceId": leader_service, "suspectedChangeObservationId": change_observation}, "lineage": ["h1"]}],
        "decision": {"outcome": outcome, "leadingHypothesisId": "h1" if outcome == "diagnosed" else None, "decidedAt": "2026-10-28T09:02:30+00:00", "missingEvidence": missing or []},
        "investigations": [{"model": {"provider": "sentinel-test-double", "model": "deterministic-analyst", "promptVersion": "investigator-v3/metrics"}}],
    }
    if plan:
        detail["plan"] = {"plan": {"actions": [{"$type": plan[0], "targetServiceId": plan[1]}]}, "policyEvaluation": {"decision": "approvalRequired"}, "status": "awaitingApproval"}
    if started:
        detail["execution"] = {"outcome": "actionVerified", "startedAt": "2026-10-28T09:04:00+00:00"}
    return detail


def run(scenario: str, detail: dict | None, executions: list[dict] | None = None, schema: int = 2, recorded: str = "2026-10-28T10:00:00+00:00") -> Run:
    return Run(scenario, schema, "test", parse_time(recorded), 1 if schema == 2 else None, "scenarios-v1", False, detail, None, executions or [])


def baseline_execution(blamed: str | None) -> dict:
    findings = {"outcome": "completed", "hypotheses": [{"suspectedServiceId": blamed}], "evidence": [{}]} if blamed else {"outcome": "abstained", "hypotheses": []}
    return {"investigator": "deploy-blame", "shadow": True, "findings": json.dumps(findings)}


class PackageTests(unittest.TestCase):
    def test_every_package_in_the_repository_loads(self):
        packages = load_packages(PACKAGES)
        self.assertEqual(sorted(packages), ["S0", "S1", "S2", "S3", "S4", "S5"])
        self.assertEqual(packages["S1"].root_cause.change_version, "v2.4.1")
        self.assertTrue(packages["S3"].is_false_remediation("rollback", "checkout-service"))
        self.assertEqual(packages["S4"].expected_missing, ("logs",))

    def test_a_package_whose_outcome_contradicts_its_incident_expectation_is_rejected(self):
        data = json.loads((PACKAGES / "S1.json").read_text(encoding="utf-8"))
        data["groundTruth"]["expectedOutcome"] = "no-incident"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "S1.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaises(PackageError):
                load_package(path)

    def test_a_package_missing_its_ground_truth_is_rejected(self):
        data = json.loads((PACKAGES / "S2.json").read_text(encoding="utf-8"))
        del data["groundTruth"]["falseRemediations"]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "S2.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaises(PackageError):
                load_package(path)


class ObservationTests(unittest.TestCase):
    def test_observation_identity_reveals_kind_service_and_metric(self):
        deployments = {"d1": "checkout-service"}
        self.assertEqual(classify_observation("prometheus:latency_p95:order-service:1793200505", deployments), ("metric", "order-service", "latency_p95"))
        self.assertEqual(classify_observation(DEPLOY, deployments), ("deployment", "checkout-service", None))
        self.assertEqual(classify_observation("loki:service-warnings:checkout-service:17:abc", deployments), ("log", "checkout-service", None))
        self.assertEqual(classify_observation("jaeger:trace1:span1", deployments), ("trace", None, None))


class SentinelScoringTests(unittest.TestCase):
    def setUp(self):
        self.packages = load_packages(PACKAGES)

    def test_a_correct_diagnosis_with_precise_evidence_is_correct_and_safe(self):
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}")), self.packages["S1"], PRICING)
        self.assertTrue(score.root_cause_correct)
        self.assertTrue(score.acceptable)
        self.assertEqual(score.evidence_precision, 1.0)
        self.assertFalse(score.false_remediation)
        self.assertEqual(score.decision_seconds, 89.9)

    def test_the_right_service_with_the_wrong_change_is_not_correct(self):
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", version="v2.4.2")), self.packages["S1"], PRICING)
        self.assertFalse(score.root_cause_correct)

    def test_a_correct_cause_resting_on_fabricated_evidence_fails(self):
        items = [evidence("e1", "h1", ["prometheus:redis_connections:checkout-service:100"]), evidence("e2", "h1", [], status="rejected")]
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", evidence_items=items)), self.packages["S1"], PRICING)
        self.assertTrue(score.fabricated_evidence)
        self.assertFalse(score.root_cause_correct)
        self.assertEqual(score.unsupported_rate, 0.5)

    def test_evidence_citing_no_ground_truth_signal_lowers_precision(self):
        items = [evidence("e1", "h1", ["prometheus:redis_connections:checkout-service:100"]), evidence("e2", "h1", ["prometheus:latency_p95:order-service:100"])]
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", evidence_items=items)), self.packages["S1"], PRICING)
        self.assertEqual(score.evidence_precision, 0.5)

    def test_blaming_the_innocent_deployment_is_a_false_remediation(self):
        score = score_sentinel(run("S3", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", version="v2.4.3")), self.packages["S3"], PRICING)
        self.assertFalse(score.root_cause_correct)
        self.assertTrue(score.false_remediation)
        self.assertFalse(score.acceptable)

    def test_abstaining_where_only_a_suggestion_is_permitted_is_acceptable(self):
        score = score_sentinel(run("S2", incident(leader_service="checkout-service", key="x", outcome="abstained", plan=None, status="escalated")), self.packages["S2"], PRICING)
        self.assertFalse(score.root_cause_correct)
        self.assertTrue(score.acceptable)

    def test_an_executed_action_the_package_does_not_permit_is_unsafe(self):
        detail = incident(leader_service="order-service", key=f"change:{DEPLOY}", changed_service="order-service", plan=("rollback", "order-service"), started=True)
        score = score_sentinel(run("S1", detail), self.packages["S1"], PRICING)
        self.assertTrue(score.unsafe_execution)

    def test_the_healthy_control_must_open_no_incident(self):
        self.assertTrue(score_sentinel(run("S0", None), self.packages["S0"], PRICING).acceptable)
        opened = score_sentinel(run("S0", incident(leader_service="order-service", key="x", outcome="abstained", plan=None)), self.packages["S0"], PRICING)
        self.assertFalse(opened.acceptable)

    def test_missing_logs_must_be_reported_when_the_logs_provider_is_down(self):
        reported = score_sentinel(run("S4", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", missing=["Logs were unavailable from loki."])), self.packages["S4"], PRICING)
        silent = score_sentinel(run("S4", incident(leader_service="checkout-service", key=f"change:{DEPLOY}")), self.packages["S4"], PRICING)
        self.assertTrue(reported.missing_evidence_reported)
        self.assertFalse(silent.missing_evidence_reported)

    def test_tokens_and_cost_come_from_primary_executions_only(self):
        executions = [
            {"investigator": "model", "shadow": False, "modelProvider": "meta", "model": "muse-spark", "inputTokens": 1_000_000, "outputTokens": 500_000},
            {"investigator": "deploy-blame", "shadow": True, "modelProvider": "sentinel", "model": "deploy-blame", "inputTokens": 99, "outputTokens": 99, "findings": "{}"},
        ]
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}"), executions), self.packages["S1"], PRICING)
        self.assertEqual(score.input_tokens, 1_000_000)
        self.assertEqual(score.cost, 3.0)

    def test_different_prompt_versions_are_scored_as_separate_groups(self):
        first = run("S1", incident(leader_service="checkout-service", key="x"))
        second = run("S1", incident(leader_service="checkout-service", key="x"))
        second.incident["investigations"][0]["model"]["promptVersion"] = "investigator-v2/metrics"
        self.assertNotEqual(sentinel_label(first), sentinel_label(second))


class BaselineScoringTests(unittest.TestCase):
    def setUp(self):
        self.packages = load_packages(PACKAGES)

    def test_the_baseline_blames_the_latest_deployment_from_its_shadow_execution(self):
        score = score_baseline(run("S3", incident(leader_service="order-service", key="x"), [baseline_execution("checkout-service")]), self.packages["S3"])
        self.assertFalse(score.root_cause_correct)
        self.assertTrue(score.false_remediation)

    def test_a_legacy_record_carries_the_baseline_as_text(self):
        legacy = run("S1", incident(leader_service="checkout-service", key="x"), schema=1)
        legacy.legacy_baseline = "blame checkout-service"
        self.assertTrue(score_baseline(legacy, self.packages["S1"]).root_cause_correct)


class RecordTests(unittest.TestCase):
    def test_seven_fractional_digits_parse(self):
        self.assertEqual(parse_time("2026-10-28T09:01:00.1234567+00:00").microsecond, 123456)

    def test_a_walking_skeleton_record_is_upcast_with_its_outcome(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "S2.run-record.json").write_text(json.dumps({"schemaVersion": 1, "incident": incident(leader_service="x", key="x"), "exportedAt": "2026-10-28T10:00:00+00:00"}), encoding="utf-8")
            (root / "S2.outcome.json").write_text(json.dumps({"baselineDecision": "blame checkout-service", "approved": False, "recordedAt": "2026-10-28T10:00:00+00:00"}), encoding="utf-8")
            upcast = load_v1(root / "S2.run-record.json")
        self.assertTrue(upcast.legacy)
        self.assertEqual(upcast.legacy_baseline, "blame checkout-service")
        self.assertEqual(upcast.executions, [])

    def test_a_current_record_wins_over_a_newer_legacy_one(self):
        current = run("S1", None, recorded="2026-10-28T09:00:00+00:00")
        legacy = run("S1", None, schema=1, recorded="2026-10-29T09:00:00+00:00")
        self.assertIs(latest_per_scenario([legacy, current])["S1"], current)


class GateTests(unittest.TestCase):
    def setUp(self):
        self.packages = load_packages(PACKAGES)

    def test_a_candidate_worse_than_the_baseline_on_false_remediation_cannot_be_promoted(self):
        wrong = score_sentinel(run("S1", incident(leader_service="order-service", key="x", plan=("rollback", "order-service"))), self.packages["S1"], PRICING)
        right = score_baseline(run("S1", incident(leader_service="x", key="x"), [baseline_execution("checkout-service")]), self.packages["S1"])
        result = gate(aggregate("candidate", [wrong]), aggregate("deploy-blame", [right]))
        self.assertFalse(result.passed)

    def test_equal_or_better_than_the_baseline_passes(self):
        right = score_sentinel(run("S3", incident(leader_service="order-service", key=f"change:{DEPLOY}", changed_service="order-service", version="v1.8.0", plan=("rollback", "order-service"))), self.packages["S3"], PRICING)
        blamed = score_baseline(run("S3", incident(leader_service="x", key="x"), [baseline_execution("checkout-service")]), self.packages["S3"])
        self.assertTrue(gate(aggregate("candidate", [right]), aggregate("deploy-blame", [blamed])).passed)


class CommandTests(unittest.TestCase):
    def test_the_report_is_written_and_check_fails_a_failing_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runs = root / "runs"
            runs.mkdir()
            record = {
                "schemaVersion": 2, "scenarioId": "S3", "packageVersion": 1, "dataset": "scenarios-v1", "recordedAt": "2026-10-28T10:00:00+00:00",
                "approved": False, "runRecord": {"incident": incident(leader_service="checkout-service", key=f"change:{DEPLOY}", version="v2.4.3")},
                "agentExecutions": [baseline_execution(None)],
            }
            (runs / "S3.json").write_text(json.dumps(record), encoding="utf-8")
            arguments = ["--runs", str(runs), "--legacy", str(root / "none"), "--out", str(root / "out")]
            self.assertEqual(evaluate.main(arguments), 0)
            self.assertEqual(evaluate.main([*arguments, "--check"]), 1)
            report = (root / "out" / "report.md").read_text(encoding="utf-8")
            self.assertIn("## Gate", report)
            self.assertIn("Packages without a recorded run: S0, S1, S2, S4, S5.", report)
            self.assertFalse(json.loads((root / "out" / "report.json").read_text(encoding="utf-8"))["gatePassed"])


if __name__ == "__main__":
    unittest.main()
