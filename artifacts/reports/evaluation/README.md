# Baseline evaluation

Generated output of the Phase 07 [evaluation harness](../../../scripts/evaluation/README.md). Do not edit these files by hand.

- `runs/`: one schema-2 run record per scenario package, written by the end-to-end suites (`GoldenPathTests` and `FailureScenarioTests` in `deploy/local/Sentinel.EndToEnd.Tests`). Each holds Runtime's run record (projection, version manifest and incident memory) and every agent execution, including the shadow deploy-blame baseline.
- `report.md` and `report.json`: Sentinel and the baseline scored against the [scenario packages](../../../reference-system/scenarios/packages/), with the promotion gate.

Regenerate the runs with the end-to-end suites, then the report:

```text
dotnet test deploy/local/Sentinel.EndToEnd.Tests --filter "FullyQualifiedName~GoldenPathTests|FullyQualifiedName~FailureScenarioTests"
python scripts/evaluation/evaluate.py
```

Runs are made with the deterministic model test double unless a live model is configured; the report names the model and prompt version of every group it scores.
