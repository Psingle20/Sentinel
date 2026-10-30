# Walking skeleton comparison report

Generated output of the Phase 01A end-to-end suite: one record per scenario run (`S1`–`S3`), Sentinel's outcome next to the shadow deploy-blame baseline's, scored against the scenario manifests in `reference-system/scenarios/manifests`. Only runs with the model test double are recorded here, so the records are reproducible; a run with a live model is recorded in [the evaluation runs](../evaluation/runs/) as `<scenario>.<provider>.json` and scored in the [evaluation report](../evaluation/report.md).

| File | Content |
| --- | --- |
| `comparison.md` | The comparison table, regenerated from every `*.outcome.json` after each scenario test |
| `<scenario>.outcome.json` | Sentinel's and the baseline's outcome for one run |
| `<scenario>.run-record.json` | The incident's exported run record: timeline, evidence, hypotheses, decision, plan, execution and recovery |

## Regenerate

From the project directory, with Docker running:

```powershell
dotnet test deploy/local/Sentinel.EndToEnd.Tests --filter "FullyQualifiedName~GoldenPathTests"
```

The suite starts the whole local system from the AppHost with empty databases, runs each scenario from a reset, approves `S1` in Sentinel.Web with Playwright and leaves `S2` and `S3` at decision and plan, as Phase 01A specifies.

## Provenance

- Investigator: the deterministic model test double (`sentinel-test-double/deterministic-analyst`, ADR-015) until the Muse Spark details are configured. Its results prove the pipeline, not reasoning quality.
- Baseline: `deploy-blame`, run in shadow by AgentWorker on every investigation (`reference-system/scenarios/baseline/deploy-blame.md`).
- Timestamps inside each record give when it was produced. Records are overwritten by the next run of the same scenario.
