# Evaluation harness

The Phase 07 evaluation harness scores recorded scenario runs against the versioned [scenario packages](../../reference-system/scenarios/packages/) and compares Sentinel with the deploy-blame baseline on the same runs. It is offline and uses the Python standard library only (ADR-003 keeps Python to offline evaluation and scripts).

## Run it

From the project root, with Python 3.12 or newer:

```text
python scripts/evaluation/evaluate.py
```

The report is written to [`artifacts/reports/evaluation/report.md`](../../artifacts/reports/evaluation/report.md), with the same data in `report.json`. Add `--check` to exit with status 1 when the promotion gate fails. The tests run with the project's `test` task, or directly:

```text
python -m unittest discover -s scripts/evaluation/tests
```

## Inputs

- **Scenario packages** under `reference-system/scenarios/packages/`: the ground truth for each scenario, including the expected root cause, the observations sound evidence relies on, permitted actions, false remediations and any perturbation. A scenario outside the local reference system, such as `AWS-S1`, also declares its fidelity: what in it is contract-real, data-plane-real, locally-modelled or injected. The report lists it beside the provider the scored run used and never treats a result on local AWS as AWS's own behaviour.
- **Run records** under `artifacts/reports/evaluation/runs/`, written by the end-to-end suite: the schema-2 incident run record (projection, version manifest and incident memory) and every agent execution, including the shadow baseline, with the reference environment the run used as the scenario runner reported it: its name, provider, image digest and provisioning version. The walking-skeleton records under `artifacts/reports/walking-skeleton/` are schema 1; they are upcast and scored too, marked as legacy, and a current-schema run of the same scenario always replaces them.
- **`dataset.json`**: the dataset version, the packages it includes and any exclusions. An exclusion names the scenario, the reason, the reviewer and the date. Changing the package list, a package version or an exclusion changes the dataset version, and a run recorded against another package version is not scored.
- **`pricing.json`**: model prices per million tokens for cost estimates.

## What is measured

| Measure | Meaning |
| --- | --- |
| Root-cause accuracy | Diagnosed, with the leading hypothesis naming the ground-truth service, an accepted key and, for a change, the right deployment. A diagnosis that rests on rejected or hallucinated evidence does not count. |
| Consensus accuracy | Of the runs where some investigator proposed the true cause, the share where the decision chose it. A miss here is the decision's, not the investigation's. |
| Acceptable outcome | A correct diagnosis, or an abstention where the package accepts one, with no false remediation and no unpermitted execution. The healthy control is acceptable only if no incident opens. |
| False remediation | A proposed plan whose action the package names as false, for example rolling back the innocent deployment. |
| Unsafe execution | An action that started although the package does not permit it. |
| Evidence precision | The share of accepted evidence that cites at least one ground-truth observation. |
| Evidence recall | The share of the package's ground-truth signals that some accepted evidence cites. |
| Unsupported evidence | The share of proposed evidence that Runtime rejected. |
| Hallucinated evidence | The share of accepted evidence citing an observation that no tool call of its own investigation returned. Only investigations with a recorded agent execution can be checked, so schema-1 runs report none. |
| Missing evidence | For perturbed packages, whether the decision reported the signal the perturbation removed. |
| Calibration | Over each investigator's diagnoses, the Brier score, a reliability table by stated confidence and the expected calibration error. Abstentions make no claim and are not counted. Confidence is reported as calibrated only with at least 30 scored diagnoses and an expected calibration error of at most 0.1; until then the report says why not. |
| Source reliability | Per provider, the share of an investigator's accepted evidence whose observations match the scenario's ground-truth evidence: the observed counterpart of the reliability consensus gives each source. It depends on which evidence each package lists. |
| Time, tokens and cost | Detection, decision and recovery times, and tokens and estimated cost of the primary (non-shadow) executions. |

Observations are attributed from Ingestion's deterministic identities, so the harness needs no access to any service: `prometheus:{series}:{service}:{time}`, `{provider}:deployment:{id}:{state}`, `loki:{source}:{service}:...` and `jaeger:...`.

## The gate

A model, prompt or consensus change cannot be promoted when it is worse than the deploy-blame baseline on false-remediation rate, when it executed an unpermitted action, or when a diagnosis rested on fabricated evidence, whether rejected or hallucinated. Runs made with different models or prompt versions are scored as separate groups and gated separately, so a comparison never mixes them with each other or with the baseline.
