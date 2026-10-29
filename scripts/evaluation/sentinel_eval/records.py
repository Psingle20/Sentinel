"""Scenario run records, upcast to one shape.

Schema 2 is written by the end-to-end suite under
artifacts/reports/evaluation/runs: the incident run record (projection,
version manifest and memory) plus every agent execution. Schema 1 is the
Phase 01A walking-skeleton pair of files, which carries no agent executions
and no versions; it stays readable as the first baseline data.
"""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path


_EARLIEST = datetime(1970, 1, 1, tzinfo=timezone.utc)


@dataclass
class Run:
    scenario_id: str
    schema_version: int
    source: str
    recorded_at: datetime | None
    package_version: int | None
    dataset: str | None
    approved: bool
    incident: dict | None
    versions: dict | None
    executions: list[dict] = field(default_factory=list)
    scenario_started_at: datetime | None = None
    legacy_baseline: str | None = None

    @property
    def legacy(self) -> bool:
        return self.schema_version < 2


def parse_time(value: str | None) -> datetime | None:
    if not value:
        return None
    # .NET writes up to seven fractional digits; Python accepts six.
    text = value.replace("Z", "+00:00")
    if "." in text:
        head, rest = text.split(".", 1)
        digits = re.match(r"[0-9]*", rest).group(0)
        zone = rest[len(digits):]
        text = f"{head}.{digits[:6].ljust(6, '0')}{zone}"
    return datetime.fromisoformat(text)


def load_v2(path: Path) -> Run:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 2:
        raise ValueError(f"{path.name}: expected an evaluation run record with schemaVersion 2")
    record = data.get("runRecord") or {}
    return Run(
        scenario_id=data["scenarioId"],
        schema_version=2,
        source=str(path),
        recorded_at=parse_time(data.get("recordedAt")),
        package_version=data.get("packageVersion"),
        dataset=data.get("dataset"),
        approved=bool(data.get("approved")),
        incident=record.get("incident"),
        versions=record.get("versions"),
        executions=list(data.get("agentExecutions", [])),
        scenario_started_at=parse_time(data.get("scenarioStartedAt")),
    )


def load_v1(record_path: Path) -> Run:
    """Upcasts a walking-skeleton record and its outcome file to the current shape."""
    scenario = record_path.name.split(".", 1)[0]
    record = json.loads(record_path.read_text(encoding="utf-8"))
    outcome_path = record_path.with_name(f"{scenario}.outcome.json")
    outcome = json.loads(outcome_path.read_text(encoding="utf-8")) if outcome_path.exists() else {}
    return Run(
        scenario_id=scenario,
        schema_version=int(record.get("schemaVersion", 1)),
        source=str(record_path),
        recorded_at=parse_time(outcome.get("recordedAt") or record.get("exportedAt")),
        package_version=None,
        dataset=None,
        approved=bool(outcome.get("approved")),
        incident=record.get("incident"),
        versions=record.get("versions"),
        legacy_baseline=outcome.get("baselineDecision"),
    )


def load_runs(runs_directory: Path | None, legacy_directory: Path | None) -> list[Run]:
    runs: list[Run] = []
    if runs_directory and runs_directory.exists():
        runs.extend(load_v2(path) for path in sorted(runs_directory.glob("*.json")))
    if legacy_directory and legacy_directory.exists():
        runs.extend(load_v1(path) for path in sorted(legacy_directory.glob("*.run-record.json")))
    return runs


def latest_per_scenario(runs: list[Run]) -> dict[str, Run]:
    """The newest run of each scenario; a current-schema run always wins over a legacy one."""
    chosen: dict[str, Run] = {}
    for run in runs:
        current = chosen.get(run.scenario_id)
        key = (run.schema_version, run.recorded_at or _EARLIEST)
        if current is None or key > (current.schema_version, current.recorded_at or _EARLIEST):
            chosen[run.scenario_id] = run
    return chosen
