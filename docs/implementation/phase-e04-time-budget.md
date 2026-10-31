# Phase E04 – Time budget and speed

Enhancement phase. Design source: [Sentinel time budget](../architecture/time-budget.md) (Proposed, 2026-10-31). This document is the delivery view: what is built, in which order, how it is measured and when it is done. The budget document holds the measurements, the targets and the design; nothing here repeats them.

## Outcome

A golden scenario runs from quiet to resolved in about four minutes on a developer machine instead of six, the seven core end-to-end tests finish in half an hour instead of fifty minutes, the stack starts in a recorded and bounded time without the start-up failures seen so far, and the `test` task stops rebuilding and restarting what has not changed. Every decision still cites the same evidence, production defaults and SLOs are untouched, and the speed is held by a recorded budget rather than by habit.

## Primary service ownership

- `deploy/local/Sentinel.AppHost` owns the local profile and the Temporal and web-app start-up choices.
- `Sentinel.Ingestion` and `Sentinel.AgentWorker` own the investigation range and completeness changes; `Sentinel.Runtime` owns detection confirmation, the verifier's start condition and streaming recovery evaluation.
- `deploy/local/Sentinel.EndToEnd.Tests` owns the quiet check, `S0` and the recorded timings.
- `scripts/check.ps1` and the test projects own the `test` task changes.
- `Sentinel.Gateway`, `Sentinel.Executor`, the contracts and the web app change nothing.

## Entry evidence required

- Run records with timestamps for every phase exist for `S0` to `S8` ([evaluation runs](../../artifacts/reports/evaluation/runs/)), so each change can be measured against a baseline.
- Runtime emits detection delay and time to decision ([RuntimeTelemetry](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Diagnostics/RuntimeTelemetry.cs)), so the budget is observable without new instrumentation.
- The end-to-end suite passes on the current code, so a regression after a timing change is attributable.

## Workstreams

The budget document's tiers are the workstreams. Tier 1 is configuration applied by the AppHost and needs no service code; Tier 2 changes the pipeline and must prove it keeps the evidence; Tier 3 is the suite, start-up and the `test` task.

| Tier | Work | Evidence when done |
| --- | --- | --- |
| 1 – Local profile | `Sentinel:Profile=local` in the AppHost, applying the budget document's profile table through environment overrides; the suite and the `run` task select it; manifests' expected timings adjusted for the 10 s rollout; nothing in any `appsettings.json` changes | `S1` in the suite under 4.5 minutes; investigations under 15 s with the test double; the baseline report unchanged on every scenario |
| 1 – Baselines recorded | Start-up time, per-test time and the `test` task's wall time added to the ledger's recorded runs | Three new lines in the ledger |
| 2 – Investigation range and verifier | Query range ends at detection minus the source's configured lag; the verifier starts when metrics, changes and topology have reported; ADR-016 amended | Opened-to-decision under 25 s with the test double; same findings and decisions on `S1` to `S8`; the falsifier tests pass |
| 2 – Streaming recovery and confirmed detection | Recovery criteria evaluated per sample after the window opens; detection on one sample confirmed by the next when onset is already crossed; `S0` false-positive check | Action-verified-to-recovered under 75 s; `S0` opens no incident across ten runs |
| 2 – Live model budget | Per-specialist latency metric; tool-call budgets per role; concurrent independent tool calls within a specialist | Median specialist latency under 45 s on the `meta` profile over `S1` to `S3`, same diagnoses |
| 3 – Suite | Quiet check bounded by the 30 s `ClearAfter` or an operator close; `S0` shortened; `next build` and `next start` for the suite's web app | Quiet check under 60 s after every scenario; seven core tests under 30 minutes |
| 3 – Start-up | Temporal dev server for the ephemeral profile, pinned; start-up time recorded per run | Start-up under 90 s; no start-up failure in ten consecutive runs |
| 3 – `test` task | Testcontainers reuse across the seven container projects; `--no-build` after one build; Playwright rebuilds only on changed inputs and runs its projects in parallel | Recorded wall time at least a third lower than the baseline |

## In scope

- Everything the table lists.
- A "Recorded timings" subsection in the ledger that the suite's output can be copied into, so the budget stays measured.
- The budget document's tables updated with the achieved numbers as each tier lands.

## Primary deliverables

1. The local profile in the AppHost and the manifests' adjusted timings.
2. The ADR-016 amendment and the investigation range, verifier and recovery changes with their tests.
3. The live-model latency metric and role budgets.
4. The suite's quiet check, `S0` and web-app start changes.
5. The Temporal dev-server start-up and the recorded start-up time.
6. The `test` task changes and its recorded wall time.

## Test plan

- Before any change, record the baseline: start-up time, each core test's duration, the `test` task's wall time, and the per-phase durations from the run records.
- After each tier, rerun the seven core end-to-end tests and regenerate the evaluation report; every scenario must keep its decision, plan and false-remediation result, and the report must still pass its gate.
- Pipeline changes get unit tests beside the owning code: range computation per source lag, verifier start condition with late findings, per-sample recovery evaluation including a criterion that holds and then fails inside the window, and confirmed detection against `S0`'s noise.
- Ten consecutive stack starts with the ephemeral profile, each recorded, for the start-up tier.
- The `test` task is timed before and after, on the same machine, with containers warm.

## Exit criteria

- The budget document's table is met with the test double: onset to opened at most 20 s, opened to decision at most 25 s, action verified to recovery at most 75 s, `S1` quiet to resolved at most 4 minutes, quiet check at most 60 s, seven core tests at most 30 minutes, start-up at most 90 s and recorded.
- Live specialists have a recorded median at most 45 s on the `meta` profile, with the same diagnoses on `S1` to `S3`.
- No production default, SLO or production late-data setting changed; the local profile is the only place the local numbers exist.
- The evaluation report passes its gate on every scenario before and after.
- The ledger records the timings and the `test` task's wall time.

## Deferred deliberately

- A 30 s burn-rate recording rule for the local fixture, until the budget above is met without it.
- Any change to the queue scenarios' detection thresholds or `S3`'s drift; those are scenario design.
- Running end-to-end scenarios in parallel against several reference systems; one developer machine carries one.
- A CI pipeline, which is Phase 00's open criterion; this phase makes the suite short enough to run there.
