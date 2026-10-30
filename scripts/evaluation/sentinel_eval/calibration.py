"""Confidence calibration and source reliability (Phase 05, deliverable 5).

A diagnosis states a confidence; across scenarios with ground truth, a calibrated
investigator is right about as often as it says it is. Calibration is measured only
over diagnoses, because an abstention makes no claim, and it is reported as
calibrated only when there are enough diagnoses to say so. Source reliability is
the share of a provider's evidence that matched the scenario's ground truth, the
observed counterpart of the reliability weights consensus uses.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from .scoring import Score

# Fewer diagnoses than this cannot tell calibration from luck.
MINIMUM_DECISIONS = 30

# The largest expected calibration error still reported as calibrated.
MAXIMUM_CALIBRATION_ERROR = 0.1

# Consensus abstains below 0.5, so diagnoses fall in these confidence ranges.
BUCKETS = [(0.5, 0.6), (0.6, 0.7), (0.7, 0.8), (0.8, 0.9), (0.9, 1.0)]


@dataclass
class Bucket:
    low: float
    high: float
    decisions: int
    mean_confidence: float | None
    accuracy: float | None


@dataclass
class Calibration:
    label: str
    decisions: int
    brier_score: float | None
    expected_calibration_error: float | None
    status: str
    reason: str
    buckets: list[Bucket] = field(default_factory=list)


@dataclass
class SourceReliability:
    label: str
    provider: str
    evidence: int
    matching: int
    precision: float


def calibrate(label: str, scores: list[Score]) -> Calibration:
    """How well one investigator's stated confidence matched how often its diagnoses were right."""
    points = [(score.confidence, bool(score.root_cause_correct)) for score in scores if score.decision == "diagnosed" and score.confidence is not None and score.root_cause_correct is not None]
    if not points:
        return Calibration(label, 0, None, None, "uncalibrated", "No diagnosis with a stated confidence was scored.")

    brier = sum((confidence - correct) ** 2 for confidence, correct in points) / len(points)
    buckets = []
    error = 0.0
    for low, high in BUCKETS:
        # The top bucket includes a confidence of exactly 1.0.
        members = [(confidence, correct) for confidence, correct in points if low <= confidence < high or (high == BUCKETS[-1][1] and confidence == high)]
        if not members:
            buckets.append(Bucket(low, high, 0, None, None))
            continue
        confidence = sum(point[0] for point in members) / len(members)
        accuracy = sum(point[1] for point in members) / len(members)
        error += len(members) / len(points) * abs(accuracy - confidence)
        buckets.append(Bucket(low, high, len(members), round(confidence, 3), round(accuracy, 3)))

    error = round(error, 3)
    counted = f"{len(points)} scored {'diagnosis' if len(points) == 1 else 'diagnoses'}"
    if len(points) < MINIMUM_DECISIONS:
        status, reason = "uncalibrated", f"{counted}; at least {MINIMUM_DECISIONS} are needed to call confidence calibrated."
    elif error > MAXIMUM_CALIBRATION_ERROR:
        status, reason = "uncalibrated", f"Expected calibration error {error:.3f} exceeds {MAXIMUM_CALIBRATION_ERROR:.2f}."
    else:
        status, reason = "calibrated", f"Expected calibration error {error:.3f} over {counted}."
    return Calibration(label, len(points), round(brier, 3), error, status, reason, buckets)


def reliability(label: str, scores: list[Score]) -> list[SourceReliability]:
    """Per provider, how much of one investigator's accepted evidence matched the scenarios' ground truth."""
    totals: dict[str, list[int]] = {}
    for score in scores:
        for provider, (evidence, matching) in score.provider_evidence.items():
            total = totals.setdefault(provider, [0, 0])
            total[0] += evidence
            total[1] += matching
    return [SourceReliability(label, provider, evidence, matching, round(matching / evidence, 3)) for provider, (evidence, matching) in sorted(totals.items()) if evidence > 0]
