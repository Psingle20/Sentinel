# Sentinel time budget

- Status: Proposed
- Last reviewed: 2026-10-31, from the recorded runs of 2026-10-30
- Delivery: [Phase E04 – Time budget and speed](../implementation/phase-e04-time-budget.md); the [phase status ledger](../implementation/phase-status.md#phase-e04--time-budget-and-speed) records progress
- Related: [service-level objectives](service-level-objectives.md), [ADR-016](../decisions/ADR-016-event-time-and-late-data.md), [ADR-005](../decisions/ADR-005-durable-workflow-engine.md), [ADR-010](../decisions/ADR-010-local-orchestration.md)

## Purpose

A golden-scenario run takes a little over six minutes on a developer machine with the deterministic test double and over seven with live models, and the seven core end-to-end tests take fifty minutes. This document says where that time goes, measured from the run records rather than estimated, which parts are physics the fixture needs, which are configuration chosen for production backends and merely inherited locally, and which are design choices that can be made faster without weakening a decision. It then sets the budget Sentinel should run to locally and the order in which to reach it.

The rule throughout: **a faster run must never be a less careful one.** Nothing here shortens the evidence a decision cites, removes a wait that protects against late data in production, or lets recovery be declared before the signals can have recovered.

## Where the time goes

### One golden scenario, start to resolved

Measured from the `S1` run records of 2026-10-30 ([test double](../../artifacts/reports/evaluation/runs/S1.json), [live Meta models](../../artifacts/reports/evaluation/runs/S1.meta.json)). The scenario starts when the runner activates it after its warm-up.

| Phase | Test double | Live models | What sets it |
| --- | --- | --- | --- |
| Warm-up before the fault | 75 s standalone, 20 s in the suite | same | Manifest `warmupSeconds`; the suite shortens it after its own quiet check |
| Fault to symptom onset | ~70 s | same | Fixture physics: checkout v2.4.1 leaks one connection per second session at 10 requests per second until the reference Redis nears `maxclients` 200 |
| Onset to incident opened | 31 s | 24 s | Metric export every 5 s, `rate[1m]` ramp, Prometheus evaluation every 5 s, Ingestion `IngestLag` 10 s and `PollInterval` 5 s, detection `RequiredSamples` 2 at `MetricStep` 5 s |
| Investigations, all specialists | 39 s | 95 s | Test double: completeness waits, not reasoning; live: model latency per specialist 15 to 74 s |
| Verifier, after the specialists | 19 s | 19 s | Deterministic verifier; it starts only when all six specialists have reported |
| Decision to approval | 34 s | 25 s | The browser journey in the suite; a person in real use |
| Approval to action verified | 25 s | 23 s | Deploy controller `RolloutSeconds` 20, Executor `VerifyPollInterval` 2 s |
| Action to recovery evaluated | 105 s | 115 s | `ObservationWindow` 90 s, `EvaluationLag` 15 s, `RecoveryRetryInterval` 10 s |
| Scenario start to resolved | 379 s | 437 s | |

Three findings follow from the table.

1. **With the test double, investigations are waiting, not thinking.** The double answers in milliseconds, yet metrics and logs take 19 s and traces 43 s in several runs (`S2`, `S4`, `S6`, `S8`). That is `PatientObservationQueries` waiting for Ingestion's `ObservedThrough` to reach the end of the query range: logs and traces are read 30 s behind the present (`LogLag`, `TraceLag`) and the wait allows up to 30 s per query and 45 s per investigation. The lags are right for a production Loki or Jaeger; locally the indexes are seconds behind.
2. **Recovery is the single largest block after onset,** at 105 to 116 s, and 60 s of it is physics: the burn rate is a `rate[1m]`, so it cannot fall below 1 until a full minute after the rollback takes effect. The other 45 s are the window's extra 30 s, the 15 s evaluation lag and the 10 s retry granularity.
3. **The verifier is serial.** It adds 16 to 19 s after the slowest specialist, although it needs only the metrics and changes findings to run its falsifiers.

### Queue scenarios

`S6` and `S7` take 74 to 76 s from onset to incident where the SLO scenarios take 20 to 31 s. Their detection rule is `queue_oldest_message_age` above 60, so by definition the incident cannot open until a message has waited a minute. `S3` takes 240 s from start to onset because its drift is 4 ms per second of serving. These are scenario design, not pipeline latency; they are listed so that nobody tunes the pipeline to fix them.

### The end-to-end suite

Seven core tests in 50 minutes, 17 facts in all today. Per test, around the scenario itself:

- **The quiet check.** Reset, stop the batch, then wait for a healthy minute of the checkout SLO (at least 10 samples of a rule evaluated every 5 s), then wait until every incident is `Resolved`. An incident that ended `Escalated` or `AwaitingApproval` resolves only when its symptom has stayed clear for `ClearAfter`, two minutes, so the quiet check after `S2`, `S3`, `S5`, `S7` and `S8` waits two to three minutes.
- **The control scenario.** `S0` runs its full 180 s, then waits a further 60 s before asserting that nothing opened.
- **Polling.** `Eventually` polls every 2 to 5 s; it adds seconds, not minutes.
- **Start-up, once per collection.** PostgreSQL, the messaging Redis, Temporal through `temporalio/auto-setup`, which creates its schemas on every ephemeral start, five services that wait for Temporal and Prometheus, the reference system and Sentinel.Web under `next dev`, which compiles each page on its first visit. Start-up is also where the only flakiness lives: in three of nineteen runs Temporal or Runtime never came up.

### The `test` task

`scripts/check.ps1 -Task test` runs `dotnet test` over the solution and then the web app's Playwright suite, one after the other. Seven of the 18 .NET test projects start their own PostgreSQL and Redis containers through Testcontainers, so the containers start seven times. The Playwright step rebuilds Storybook and the Next.js app on every run before its 162 visual and 41 app tests, whether or not anything changed.

## Budget

Local targets, with the test double, measured by the telemetry Sentinel already emits (`sentinel.runtime.incident.detection_delay`, `sentinel.runtime.incident.time_to_decision`) and by the run records. The production SLOs in [service-level objectives](service-level-objectives.md) do not change.

| Measure | Today | Budget |
| --- | --- | --- |
| Onset to incident opened, SLO scenarios | 20 to 31 s | 20 s |
| Incident opened to decision, test double | 39 to 62 s | 25 s |
| Incident opened to decision, live models, per specialist median | 15 to 74 s | 45 s |
| Action verified to recovery evaluated | 105 to 116 s | 75 s |
| `S1` in the suite, quiet to resolved | ~6 min | 4 min |
| Quiet check between tests | 1 to 3 min | 60 s |
| Seven core end-to-end tests | 50 min | 30 min |
| Stack start-up to every resource healthy | not recorded | recorded, then 90 s |

## Design

### A local profile, not new defaults

Every number above that is configuration is right for production backends and wrong only locally. So the change is a named profile the AppHost applies through the environment overrides it already uses (`Runtime__...`, `Ingestion__...`), selected by `Sentinel:Profile=local`, and never a change to the services' `appsettings.json`. The end-to-end suite and the developer's `run` task select it; nothing else does. The suite reads live configuration from the project folders while it runs, which is one more reason the profile lives in the AppHost and not in files.

| Setting | Production default | Local profile | Why it is safe |
| --- | --- | --- | --- |
| Ingestion `IngestLag` | 10 s | 5 s | Local Prometheus receives OTLP every 5 s; nothing arrives later |
| Ingestion `LogLag`, `TraceLag` | 30 s | 10 s | Local Loki and Jaeger index in seconds; completeness metadata still marks a short answer partial |
| AgentWorker completeness `MaxWait`, `WaitBudget`, `MaxLag` | 30 s, 45 s, 45 s | 10 s, 15 s, 20 s | With 10 s lags the first answer is complete; the wait exists for the production case |
| Runtime `ObservationWindow`, `EvaluationLag`, `RecoveryRetryInterval` | 90 s, 15 s, 10 s | 60 s, 5 s, 5 s | 60 s is the `rate[1m]` floor; below it recovery would be inconclusive by construction |
| Detection `ClearAfter` | 2 min | 30 s | Only affects when a cleared incident closes; the suite's quiet check is the beneficiary |
| Executor `VerifyPollInterval` | 2 s | 1 s | Polling granularity only |
| Deploy controller `RolloutSeconds` | 20 s | 10 s | Fixture realism; the manifests' expected timings move with it |

Expected effect on `S1` in the suite with the test double: investigations from 39 to about 12 s, recovery from 105 to about 70 s, total from about 6 to about 4.5 min, before any code change.

### Pipeline changes that keep the evidence

These change code and each needs its tests; the first two touch the late-data policy and are recorded as an amendment to ADR-016.

1. **End the investigation range at what can be known.** The query range today ends at the moment of detection, so a healthy source is always "merely late" and waited for. Ending the range at detection minus the source's configured lag makes the first answer complete without waiting, and the window still covers the onset, which is what the falsifiers need.
2. **Let the verifier start on what it needs.** The falsifiers use metrics, changes and topology. Start the verifier when those three have reported and fold a later logs or traces finding into consensus if it arrives before the decision; otherwise consensus proceeds with completeness marked. Saves the serial 16 to 19 s and, live, the wait for the slowest specialist.
3. **Judge recovery as data arrives.** Evaluate the criteria on each new sample after the window opens and resolve when every criterion has held for the window, rather than waiting for the window to end and then a lag. The window stays 60 s; the lag and retry granularity disappear.
4. **Detect on confirmation, not on two full samples.** When the onset threshold has already been crossed, one sample above the detection threshold confirmed by the next evaluation opens the incident. Saves one `MetricStep`; measure the false-positive rate on `S0` before keeping it.
5. **Live models: budget per role.** Record per-specialist latency as a metric, give metrics, logs and knowledge a lower tool-call budget than traces, topology and changes, and run a specialist's independent tool calls concurrently. The deterministic verifier is already free. Target a 45 s median per specialist on the `meta` profile.

### Suite and start-up

1. **Replace the quiet check's longest wait.** With `ClearAfter` at 30 s locally, "every incident resolved" takes under a minute. Alternatively give the suite a reset that closes the previous test's incident through Runtime's operator API, which the restart and outage tests already drive.
2. **Shorten `S0`.** Assert "no incident" during the 180 s run and drop the extra minute, or shorten the control to 120 s; the control proves the absence of false positives in change noise, which 120 s of deploys and traffic shows.
3. **Temporal dev server for local and tests.** `temporal server start-dev` in the official CLI image starts in seconds with an in-memory store, where `auto-setup` creates PostgreSQL schemas on every ephemeral start. It removes the schema set-up from the critical path and the only start-up flakiness seen so far. Keep `auto-setup` on PostgreSQL for the persistent developer profile if a durable workflow history is wanted; ADR-005 is unaffected, since Temporal remains the engine either way.
4. **Serve the built web app in the suite.** `next build` once and `next start`, instead of `next dev` compiling each page on first visit; the postmortem page timed out for exactly this reason on 2026-10-30.
5. **Record start-up time** as a line in the ledger's recorded runs, so the budget has a baseline.

### The `test` task

1. Share one PostgreSQL and one Redis across the seven Testcontainers projects with Testcontainers' reuse, keyed per run, so containers start once.
2. `dotnet build` once and `dotnet test --no-build`; today the test step rebuilds.
3. In Playwright, rebuild Storybook and the app only when their inputs changed, and run the visual and app projects as parallel workers.
4. Record the task's wall time in the ledger beside the test counts.

## Non-goals

- Changing any production default, SLO or the late-data policy for real backends.
- Shortening the `rate[1m]` SLO window or the evidence a decision cites.
- Making scenarios faster by weakening their faults; the queue scenarios' 60 s age threshold and `S3`'s drift are scenario design and stay.
- Replacing Temporal, Orleans or the outbox and inbox path for speed; their cost is milliseconds.

## Open questions

- Should the local profile also apply when a developer runs the AppHost without the suite, or stay opt-in so that local runs match production timing by default?
- Is a 30 s burn-rate recording rule worth adding locally so detection and recovery can both be measured against a shorter window, or does that make the local fixture too different from the production SLO?
- Which Temporal dev-server image is pinned, and does the Testcontainers Temporal test move to it too?
