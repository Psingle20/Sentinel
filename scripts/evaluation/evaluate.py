"""Scores recorded scenario runs against the scenario packages and writes the baseline evaluation report.

    python scripts/evaluation/evaluate.py [--check]

--check exits with status 1 when the promotion gate fails, for use before a
model, prompt or consensus change is promoted.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import asdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from sentinel_eval.packages import load_packages  # noqa: E402
from sentinel_eval.calibration import calibrate, reliability  # noqa: E402
from sentinel_eval.records import latest_per_scenario, latest_runs, load_runs  # noqa: E402
from sentinel_eval.report import aggregate, gate, render, to_json  # noqa: E402
from sentinel_eval.scoring import BASELINE, labeller, score_baseline, score_sentinel  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--packages", type=Path, default=ROOT / "reference-system" / "scenarios" / "packages")
    parser.add_argument("--runs", type=Path, default=ROOT / "artifacts" / "reports" / "evaluation" / "runs")
    parser.add_argument("--legacy", type=Path, default=ROOT / "artifacts" / "reports" / "walking-skeleton")
    parser.add_argument("--dataset", type=Path, default=Path(__file__).resolve().parent / "dataset.json")
    parser.add_argument("--pricing", type=Path, default=Path(__file__).resolve().parent / "pricing.json")
    parser.add_argument("--out", type=Path, default=ROOT / "artifacts" / "reports" / "evaluation")
    parser.add_argument("--check", action="store_true", help="exit 1 when the promotion gate fails")
    args = parser.parse_args(argv)

    dataset = json.loads(args.dataset.read_text(encoding="utf-8"))
    pricing = json.loads(args.pricing.read_text(encoding="utf-8"))
    excluded = {item["scenario"] for item in dataset.get("exclusions", [])}
    packages = {key: package for key, package in load_packages(args.packages).items() if key in dataset["packages"] and key not in excluded}

    loaded = [run for run in load_runs(args.runs, args.legacy) if run.scenario_id in packages]
    current = [run for run in loaded if run.package_version is None or run.package_version == packages[run.scenario_id].version]
    for run in loaded:
        if run not in current:
            print(f"{run.scenario_id}: run recorded against package v{run.package_version}, current is v{packages[run.scenario_id].version}; not scored", file=sys.stderr)

    # Each investigator's newest run of each scenario is scored; the deploy-blame baseline, which runs in shadow beside
    # every investigation, is scored once per scenario, from that scenario's newest run.
    label = labeller(current)
    runs = latest_runs(current, label)
    scores = [score_sentinel(run, packages[scenario_id], pricing, label) for (scenario_id, _), run in sorted(runs.items())]
    scores += [score_baseline(run, packages[scenario_id]) for scenario_id, run in sorted(latest_per_scenario(list(runs.values())).items())]

    labels = sorted({score.label for score in scores if score.investigator != BASELINE})
    candidates = [aggregate(label, [score for score in scores if score.label == label]) for label in labels]
    baseline = aggregate(BASELINE, [score for score in scores if score.investigator == BASELINE])
    groups = [*candidates, baseline]
    gates = [gate(candidate, baseline) for candidate in candidates]
    calibrations = [calibrate(label, [score for score in scores if score.label == label]) for label in labels]
    reliabilities = [item for label in labels for item in reliability(label, [score for score in scores if score.label == label])]
    missing = sorted(set(packages) - {scenario_id for scenario_id, _ in runs})

    # The environment each scenario's scored run used, beside the fidelity its package declares (Phase E01).
    environments = {scenario_id: run.environment for scenario_id, run in latest_per_scenario(list(runs.values())).items() if run.environment}
    fidelity = {key: asdict(package.fidelity) for key, package in sorted(packages.items()) if package.fidelity is not None}

    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "report.md").write_text(render(dataset, packages, scores, groups, gates, missing, calibrations=calibrations, reliabilities=reliabilities, environments=environments), encoding="utf-8")
    (args.out / "report.json").write_text(to_json(dataset, scores, groups, gates, missing, calibrations=calibrations, reliabilities=reliabilities, environments=environments, fidelity=fidelity), encoding="utf-8")
    passed = all(result.passed for result in gates)
    print(f"Scored {len(runs)} run(s) against {len(packages)} package(s); gate {'passed' if passed else 'failed'}. Report: {args.out / 'report.md'}")
    return 1 if args.check and not passed else 0


if __name__ == "__main__":
    raise SystemExit(main())
