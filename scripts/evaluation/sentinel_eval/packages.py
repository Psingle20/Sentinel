"""Scenario packages: the ground truth a run is scored against.

The JSON Schema in reference-system/scenarios/packages documents the format
for editors; this module enforces the parts scoring depends on, so a malformed
package fails loudly instead of scoring silently wrong.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

KINDS = {"control", "golden", "counterfactual", "failure", "regression"}
OUTCOMES = {"no-incident", "resolved", "escalated"}
EVIDENCE_KINDS = {"metric", "log", "trace", "deployment", "topology"}
PERTURBATIONS = {"provider-outage", "action-failure"}
MODES = {"approval-required", "suggestion-only"}
# The AWS-emulated environment proposal's fidelity model (Phase E01).
ENVIRONMENTS = {"local", "aws"}
FIDELITY_LEVELS = {"contract-real", "data-plane-real", "emulator-modelled", "injected", "unsupported"}


class PackageError(ValueError):
    """A scenario package that does not match the format."""


@dataclass(frozen=True)
class EvidenceSpec:
    kind: str
    service: str | None = None
    metric: str | None = None
    role: str | None = None


@dataclass(frozen=True)
class Action:
    action: str
    service: str
    mode: str | None = None


@dataclass(frozen=True)
class RootCause:
    service: str
    hypothesis_keys: tuple[str, ...]
    change_service: str | None = None
    change_version: str | None = None


@dataclass(frozen=True)
class FidelityLabel:
    element: str
    level: str


@dataclass(frozen=True)
class Fidelity:
    """What in a scenario is real, emulated and injected, and so which claims its result supports."""

    environment: str
    statement: str
    labels: tuple[FidelityLabel, ...]


@dataclass(frozen=True)
class Package:
    id: str
    version: int
    kind: str
    manifest: str
    title: str
    expect_incident: bool
    expected_outcome: str
    abstention_acceptable: bool
    root_cause: RootCause | None
    evidence: tuple[EvidenceSpec, ...]
    permitted: tuple[Action, ...]
    false_remediations: tuple[Action, ...]
    expected_missing: tuple[str, ...] = ()
    perturbations: tuple[dict, ...] = field(default_factory=tuple)
    fidelity: Fidelity | None = None

    def is_false_remediation(self, action: str, service: str) -> bool:
        return any(remedy.action == action and remedy.service == service for remedy in self.false_remediations)

    def is_permitted(self, action: str, service: str) -> bool:
        return any(remedy.action == action and remedy.service == service for remedy in self.permitted)


def _require(condition: bool, path: Path, message: str) -> None:
    if not condition:
        raise PackageError(f"{path.name}: {message}")


def load_package(path: Path) -> Package:
    data = json.loads(path.read_text(encoding="utf-8"))
    _require(data.get("schemaVersion") == 1, path, "schemaVersion must be 1")
    for name in ("id", "version", "kind", "manifest", "title", "groundTruth"):
        _require(name in data, path, f"missing {name}")
    _require(data["kind"] in KINDS, path, f"kind {data['kind']!r} is not one of {sorted(KINDS)}")
    _require(isinstance(data["version"], int) and data["version"] >= 1, path, "version must be a positive integer")

    truth = data["groundTruth"]
    for name in ("expectIncident", "expectedOutcome", "abstentionAcceptable", "evidence", "permittedActions", "falseRemediations"):
        _require(name in truth, path, f"groundTruth is missing {name}")
    _require(truth["expectedOutcome"] in OUTCOMES, path, f"expectedOutcome {truth['expectedOutcome']!r} is not one of {sorted(OUTCOMES)}")
    _require(truth["expectIncident"] == (truth["expectedOutcome"] != "no-incident"), path, "expectIncident contradicts expectedOutcome")

    root = truth.get("rootCause")
    _require(root is not None or not truth["expectIncident"], path, "an incident is expected but no rootCause is given")
    root_cause = None
    if root is not None:
        _require(bool(root.get("hypothesisKeys")), path, "rootCause needs at least one hypothesis key")
        change = root.get("change") or {}
        root_cause = RootCause(root["service"], tuple(root["hypothesisKeys"]), change.get("service"), change.get("version"))

    evidence = []
    for spec in truth["evidence"]:
        _require(spec.get("kind") in EVIDENCE_KINDS, path, f"evidence kind {spec.get('kind')!r} is not one of {sorted(EVIDENCE_KINDS)}")
        evidence.append(EvidenceSpec(spec["kind"], spec.get("service"), spec.get("metric"), spec.get("role")))

    permitted = []
    for remedy in truth["permittedActions"]:
        _require(remedy.get("mode") in MODES, path, f"permitted action mode {remedy.get('mode')!r} is not one of {sorted(MODES)}")
        permitted.append(Action(remedy["action"], remedy["service"], remedy["mode"]))

    perturbations = tuple(data.get("perturbations", []))
    for perturbation in perturbations:
        _require(perturbation.get("kind") in PERTURBATIONS, path, f"perturbation {perturbation.get('kind')!r} is not one of {sorted(PERTURBATIONS)}")

    fidelity = _fidelity(data.get("fidelity"), path)
    _require(not data["id"].startswith("AWS-") or (fidelity is not None and fidelity.environment == "aws"), path, "an AWS scenario needs a fidelity statement for the aws environment")

    return Package(
        id=data["id"],
        version=data["version"],
        kind=data["kind"],
        manifest=data["manifest"],
        title=data["title"],
        expect_incident=truth["expectIncident"],
        expected_outcome=truth["expectedOutcome"],
        abstention_acceptable=truth["abstentionAcceptable"],
        root_cause=root_cause,
        evidence=tuple(evidence),
        permitted=tuple(permitted),
        false_remediations=tuple(Action(remedy["action"], remedy["service"]) for remedy in truth["falseRemediations"]),
        expected_missing=tuple(truth.get("expectedMissingEvidence", [])),
        perturbations=perturbations,
        fidelity=fidelity,
    )


def _fidelity(spec: dict | None, path: Path) -> Fidelity | None:
    if spec is None:
        return None
    _require(spec.get("environment") in ENVIRONMENTS, path, f"fidelity environment {spec.get('environment')!r} is not one of {sorted(ENVIRONMENTS)}")
    _require(bool(spec.get("statement")), path, "fidelity needs a statement")
    labels = []
    for label in spec.get("labels", []):
        _require(label.get("level") in FIDELITY_LEVELS, path, f"fidelity level {label.get('level')!r} is not one of {sorted(FIDELITY_LEVELS)}")
        _require(bool(label.get("element")), path, "a fidelity label needs an element")
        labels.append(FidelityLabel(label["element"], label["level"]))
    _require(bool(labels), path, "fidelity needs at least one label")
    return Fidelity(spec["environment"], spec["statement"], tuple(labels))


def load_packages(directory: Path) -> dict[str, Package]:
    packages = {}
    # Package files are named by scenario ID; matching is explicit because globbing is case-insensitive on Windows.
    for path in sorted(path for path in directory.glob("*.json") if re.fullmatch(r"(AWS-)?S[0-9]+\.json", path.name)):
        package = load_package(path)
        _require(package.id == path.stem, path, f"id {package.id!r} does not match the file name")
        packages[package.id] = package
    return packages
