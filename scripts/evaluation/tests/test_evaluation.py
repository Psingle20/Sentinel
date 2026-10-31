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
from sentinel_eval.calibration import MINIMUM_DECISIONS, calibrate, reliability  # noqa: E402
from sentinel_eval.packages import PackageError, load_package, load_packages  # noqa: E402
from sentinel_eval.records import Run, latest_per_scenario, latest_runs, load_v1, load_v2, parse_time  # noqa: E402
from sentinel_eval.report import aggregate, gate, render  # noqa: E402
from sentinel_eval.scoring import Score, classify_observation, labeller, score_baseline, score_sentinel, sentinel_label  # noqa: E402

PACKAGES = HERE.parents[2] / "reference-system" / "scenarios" / "packages"
PRICING = {"models": {"meta/muse-spark": {"inputPerMillion": 1.0, "outputPerMillion": 4.0}}, "default": {"inputPerMillion": 0, "outputPerMillion": 0}}
DEPLOY = "deploy-controller:deployment:d1:started"


def evidence(evidence_id: str, hypothesis: str, observations: list[str], status: str = "accepted") -> dict:
    return {"finding": {"evidenceId": evidence_id, "statement": "A signal.", "direction": "supports", "sourceObservationIds": observations, "hypothesisIds": [hypothesis]}, "status": status, "investigationId": "inv1"}


def model_execution(returned: list[str]) -> dict:
    return {"investigator": "model", "shadow": False, "investigationId": "inv1", "toolCalls": [{"toolName": "metric_range", "observationIds": returned}]}


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
        self.assertEqual(sorted(packages), ["AWS-S1", "AWS-S7", "S0", "S1", "S10", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9"])
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

    def test_an_aws_scenario_says_what_in_it_is_real_locally_modelled_and_injected(self):
        fidelity = load_packages(PACKAGES)["AWS-S1"].fidelity
        self.assertEqual(fidelity.environment, "aws")
        self.assertEqual({label.level for label in fidelity.labels}, {"contract-real", "data-plane-real", "locally-modelled", "injected"})

    def test_an_aws_scenario_without_a_fidelity_statement_is_rejected(self):
        data = json.loads((PACKAGES / "AWS-S1.json").read_text(encoding="utf-8"))
        del data["fidelity"]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "AWS-S1.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(PackageError, "fidelity"):
                load_package(path)

    def test_a_fidelity_level_outside_the_model_is_rejected(self):
        data = json.loads((PACKAGES / "AWS-S1.json").read_text(encoding="utf-8"))
        data["fidelity"]["labels"][0]["level"] = "aws-equivalent"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "AWS-S1.json"
            path.write_text(json.dumps(data), encoding="utf-8")
            with self.assertRaisesRegex(PackageError, "fidelity level"):
                load_package(path)


class ObservationTests(unittest.TestCase):
    def test_observation_identity_reveals_kind_service_and_metric(self):
        deployments = {"d1": "checkout-service"}
        self.assertEqual(classify_observation("prometheus:latency_p95:order-service:1793200505", deployments), ("metric", "order-service", "latency_p95"))
        self.assertEqual(classify_observation("sqs:queue_depth:queue-consumer:1793200505", deployments), ("metric", "queue-consumer", "queue_depth"))
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

    def test_a_recorded_cost_wins_and_an_older_record_is_priced_by_its_model(self):
        executions = [
            {"investigator": "model", "shadow": False, "modelProvider": "meta-wonder", "model": "muse-wonder-1.0", "inputTokens": 1_000_000, "outputTokens": 0, "estimatedCost": 0.5},
            {"investigator": "model", "shadow": False, "modelProvider": "meta", "model": "muse-spark", "inputTokens": 1_000_000, "outputTokens": 0},
        ]
        priced = {"models": {"muse-spark": {"inputPerMillion": 2.0, "outputPerMillion": 4.0}}, "default": {"inputPerMillion": 0, "outputPerMillion": 0}}
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}"), executions), self.packages["S1"], priced)
        self.assertEqual(score.cost, 2.5)

    def test_different_prompt_versions_are_scored_as_separate_groups(self):
        first = run("S1", incident(leader_service="checkout-service", key="x"))
        second = run("S1", incident(leader_service="checkout-service", key="x"))
        second.incident["investigations"][0]["model"]["promptVersion"] = "investigator-v2/metrics"
        self.assertNotEqual(sentinel_label(first), sentinel_label(second))

    def test_evidence_citing_an_observation_its_tools_never_returned_is_hallucinated_and_fails_the_diagnosis(self):
        metrics = ["prometheus:redis_connections:checkout-service:100", "prometheus:redis_connections:checkout-service:160"]
        detail = incident(leader_service="checkout-service", key=f"change:{DEPLOY}")
        score = score_sentinel(run("S1", detail, [model_execution(metrics)]), self.packages["S1"], PRICING)
        self.assertEqual(score.hallucinated_evidence, 1)
        self.assertEqual(score.hallucination_rate, 0.5)
        self.assertTrue(score.fabricated_evidence)
        self.assertFalse(score.root_cause_correct)

    def test_evidence_whose_tools_returned_every_citation_is_not_hallucinated(self):
        returned = ["prometheus:redis_connections:checkout-service:100", "prometheus:redis_connections:checkout-service:160", DEPLOY]
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}"), [model_execution(returned)]), self.packages["S1"], PRICING)
        self.assertEqual(score.hallucinated_evidence, 0)
        self.assertEqual(score.hallucination_rate, 0.0)
        self.assertTrue(score.root_cause_correct)

    def test_evidence_without_a_recorded_execution_is_not_judged_for_hallucination(self):
        score = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}")), self.packages["S1"], PRICING)
        self.assertEqual(score.hallucinated_evidence, 0)
        self.assertTrue(score.root_cause_correct)

    def test_consensus_accuracy_is_judged_only_when_an_investigator_proposed_the_true_cause(self):
        chosen = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}")), self.packages["S1"], PRICING)
        discarded = score_sentinel(run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}", outcome="abstained", plan=None)), self.packages["S1"], PRICING)
        never_proposed = score_sentinel(run("S1", incident(leader_service="order-service", key="saturation:redis")), self.packages["S1"], PRICING)
        self.assertEqual((chosen.correct_hypothesis_proposed, chosen.consensus_correct), (True, True))
        self.assertEqual((discarded.correct_hypothesis_proposed, discarded.consensus_correct), (True, False))
        self.assertEqual((never_proposed.correct_hypothesis_proposed, never_proposed.consensus_correct), (False, None))
        self.assertEqual(aggregate("sentinel", [chosen, discarded, never_proposed]).consensus_accuracy, 0.5)


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

    def test_a_live_run_is_kept_beside_the_test_doubles_and_each_keeps_its_newest(self):
        double_old = run("S1", None, recorded="2026-10-28T09:00:00+00:00")
        double_new = run("S1", None, recorded="2026-10-29T09:00:00+00:00")
        live = run("S1", None, recorded="2026-10-28T12:00:00+00:00")
        labels = {id(double_old): "double", id(double_new): "double", id(live): "live"}
        chosen = latest_runs([double_old, live, double_new], lambda item: labels[id(item)])
        self.assertEqual(sorted(chosen), [("S1", "double"), ("S1", "live")])
        self.assertIs(chosen[("S1", "double")], double_new)
        self.assertIs(chosen[("S1", "live")], live)

    def test_a_legacy_record_yields_to_a_current_run_of_any_investigator(self):
        legacy = run("S2", None, schema=1, recorded="2026-10-29T09:00:00+00:00")
        current = run("S2", None, recorded="2026-10-28T09:00:00+00:00")
        labels = {id(legacy): "legacy-double", id(current): "live"}
        self.assertEqual(list(latest_runs([legacy, current], lambda item: labels[id(item)]).values()), [current])


def diagnosis(confidence: float, correct: bool, decision: str = "diagnosed") -> Score:
    return Score("S1", "sentinel", "model", False, True, decision, confidence=confidence, root_cause_correct=correct)


def stated(confidence: float, count: int, correct: int) -> list[Score]:
    return [diagnosis(confidence, index < correct) for index in range(count)]


class CalibrationTests(unittest.TestCase):
    def test_diagnoses_right_as_often_as_they_claim_are_calibrated_once_there_are_enough(self):
        scores = stated(0.65, 20, 13) + stated(0.85, 20, 17)

        calibration = calibrate("model", scores)

        self.assertEqual(calibration.status, "calibrated")
        self.assertEqual(calibration.decisions, 40)
        self.assertEqual(calibration.expected_calibration_error, 0.0)
        self.assertEqual([(bucket.decisions, bucket.accuracy) for bucket in calibration.buckets if bucket.decisions], [(20, 0.65), (20, 0.85)])

    def test_an_overconfident_investigator_is_uncalibrated_and_says_by_how_much(self):
        calibration = calibrate("model", stated(0.9, 40, 20))

        self.assertEqual(calibration.status, "uncalibrated")
        self.assertEqual(calibration.expected_calibration_error, 0.4)
        self.assertIn("exceeds", calibration.reason)
        self.assertEqual(calibration.brier_score, 0.41)

    def test_too_few_diagnoses_are_uncalibrated_however_accurate(self):
        calibration = calibrate("model", stated(0.9, 5, 5))

        self.assertEqual(calibration.status, "uncalibrated")
        self.assertIn(str(MINIMUM_DECISIONS), calibration.reason)

    def test_an_abstention_makes_no_claim_and_is_not_counted(self):
        calibration = calibrate("model", [diagnosis(0.45, False, decision="abstained"), diagnosis(1.0, True)])

        self.assertEqual(calibration.decisions, 1)
        self.assertEqual(calibration.buckets[-1].decisions, 1, "a confidence of 1.0 falls in the top bucket")

    def test_source_reliability_sums_each_providers_evidence_across_scenarios(self):
        first = diagnosis(0.8, True)
        first.provider_evidence = {"prometheus": (4, 3), "loki": (2, 0)}
        second = diagnosis(0.7, False)
        second.provider_evidence = {"prometheus": (2, 1)}

        sources = reliability("model", [first, second])

        self.assertEqual([(item.provider, item.evidence, item.matching, item.precision) for item in sources], [("loki", 2, 0, 0.0), ("prometheus", 6, 4, 0.667)])


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
            self.assertIn("Packages without a recorded run: AWS-S1, AWS-S7, S0, S1, S10, S2, S4, S5, S6, S7, S8, S9.", report)
            self.assertFalse(json.loads((root / "out" / "report.json").read_text(encoding="utf-8"))["gatePassed"])


class LabelTests(unittest.TestCase):
    def test_a_run_in_which_no_investigation_ran_is_scored_with_the_runs_of_its_configured_provider(self):
        investigated = run("S1", incident(leader_service="checkout-service", key=f"change:{DEPLOY}"))
        investigated.configured_provider = "test-double"
        quiet = run("S0", None)
        quiet.configured_provider = "test-double"
        unrecorded = run("S0", None)
        label = labeller([investigated, quiet])
        self.assertEqual(label(quiet), label(investigated))
        self.assertEqual(label(investigated), "sentinel sentinel-test-double/deterministic-analyst@investigator-v3")
        self.assertEqual(label(unrecorded), "sentinel (no investigation ran)")


class FidelityTests(unittest.TestCase):
    def test_a_run_record_carries_the_environment_it_ran_in(self):
        environment = {"name": "aws", "provider": "floci/floci:2.3.0", "imageDigest": "sha256:0123", "provisioningVersion": "2"}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "AWS-S1.json"
            path.write_text(json.dumps({"schemaVersion": 2, "scenarioId": "AWS-S1", "packageVersion": 1, "environment": environment, "runRecord": None}), encoding="utf-8")
            self.assertEqual(load_v2(path).environment, environment)

    def test_the_report_labels_a_locally_modelled_scenario_and_never_claims_aws_equivalence(self):
        packages = load_packages(PACKAGES)
        environments = {"AWS-S1": {"name": "aws", "provider": "floci/floci:2.3.0", "imageDigest": "sha256:0123", "provisioningVersion": "2"}}
        report = render({"version": "scenarios-v2"}, packages, [], [], [], [], environments=environments)
        self.assertIn("## Environments and fidelity", report)
        self.assertIn("| AWS-S1 | aws | floci/floci:2.3.0 (sha256:0123), provisioning v2 |", report)
        self.assertIn("it is not evidence of how AWS itself behaves", report)
        self.assertNotIn("| S1 | local", report, "a local scenario declares no fidelity of its own")


if __name__ == "__main__":
    unittest.main()
