# Phase status ledger

This ledger records what each [implementation phase](README.md) has delivered and which of its exit criteria remain open, with a link to the evidence for every claim. The phase documents define the work; this file is the only place that says how far it has got. A phase is **Complete** only when every exit criterion has evidence here. Update this file in the same change that moves a phase, and date the entry.

- Last reviewed: 2026-10-30, after every test project, the evaluation harness, all six end-to-end scenarios and the restart test passed, the last in one run that completed Phase 05; see [Recorded runs](#recorded-runs).
- Evidence links point at code, tests, ADRs and generated reports inside this repository. A claim without a link is not evidence.

## Status legend

| Status | Meaning |
| --- | --- |
| Complete | Every exit criterion has linked evidence. |
| In progress | The phase's implementation is on the golden path, but at least one exit criterion is open. |
| Skeleton | Only the [Phase 01A](phase-01a-walking-skeleton.md) stub for this phase exists; deepening has not started. |
| Not started | Nothing beyond the plan exists. |

## Summary

| Phase | Status | Delivered | Open exit criteria |
| --- | --- | --- | --- |
| [00 – Foundations](#phase-00--foundations) | In progress | Five layered services and the web app, building blocks, contracts with snapshots, design system, architecture tests, 20 ADRs | CI pipeline |
| [01 – Reference system](#phase-01--reference-system) | Complete | Reference services, deploy controller, scenario runner, scenarios `S0`–`S3`, baseline, tests, trace continuity; the reference suite passed on 2026-10-30 | None |
| [01A – Walking skeleton](#phase-01a--walking-skeleton) | In progress | Golden path across all services, end-to-end suite, `S1`, `S2` and `S3` records, a live `S1` run on Muse Spark | CI check |
| [02 – Telemetry ingestion](#phase-02--telemetry-ingestion) | Complete | Six adapters with conformance tests, alerts, query contract with trust metadata, bulkhead, dead letters with tested replay, paging, source health view | None |
| [03 – Incident runtime](#phase-03--incident-runtime) | Complete | Event-sourced incidents, grains, Temporal workflow with every ADR-005 test, correlation, projections, PostgreSQL store tests | None |
| [04 – AI investigation](#phase-04--ai-investigation) | Complete | Six specialists, typed tools, Meta adapter, profiles and spend caps, findings validation, blackboard, a live run with findings | None |
| [05 – Verification and consensus](#phase-05--verification-and-consensus) | Complete | Deterministic adversarial verifier (`falsifiers-v2`), `consensus-v4` with dissent and abstention, replayable decisions, calibration and source reliability, correct diagnoses on `S1`–`S3`, with the test double and with Meta's models | None |
| [06 – Safe remediation](#phase-06--safe-remediation) | Complete | Policy as code, autonomy modes, single-use capabilities, signed decisions, operator attestations, revocation, compensation, baseline-aware recovery | None; the managed signing facility is remaining scope |
| [07 – Memory, replay and evaluation](#phase-07--memory-replay-and-evaluation) | Complete | Incident memory, replay levels 1–3, postmortems, similar incidents, retention, scenario packages, evaluation harness, baseline report | None |
| [08 – Product experience](#phase-08--product-experience) | In progress | Live updates, real identity, tenant scoping, control-plane API and pages, manual overrides, notifications, analytics, responder pages, the responder journeys on the real system | The administrator journey on the real system, identity provider |
| [09 – Production hardening](#phase-09--production-hardening) | In progress | Resilience and health, local runbooks including key rotation and restore, threat model, failure scenarios `S4` and `S5`, a frozen message broker, Sentinel SLOs | Deployment definition, service-to-service authentication, SLO alerting, a whole-system restore drill |
| [10 – Scale and evolution](#phase-10--scale-and-evolution) | Not started | Nothing; requires measured need | Not applicable yet |
| [E01 – AWS-emulated reference environment](#phase-e01--aws-emulated-reference-environment) | In progress | Stage 0: Floci pinned (ADR-021). Stage 0b: the queue scenarios `S6`, `S7` and `S8`, each diagnosed end to end where the baseline rolls back an innocent deployment. Stage 1: the AWS environment in Aspire, with the consumer on ECS and SQS, and `AWS-S0`. Stage 2: `AWS-S1` end to end through Ingestion's ECS, topology and SQS adapters and Executor's SQS quarantine, gate check 8 within its budget, and the queue scenarios in the evaluation report. Stage 3 in part: provider conformance for the AWS adapters, fidelity labels in run records and the report, the quarantine journey in the web app | Stage 3: a web journey against the real system and replay of a captured AWS run; Stage 4. Stage 5 is deferred by the owner |
| [E02 – Frontend design-system depth](#phase-e02--frontend-design-system-depth) | Complete | Form and feedback primitives with the feature code on them, the full token model under the lint rule, motion tokens, one status vocabulary, theme selection, the tablet width, interaction stories | None; Linux screenshot baselines are deferred by the owner until CI exists |

## Recorded runs

| Date | Task | Result | Notes |
| --- | --- | --- | --- |
| 2026-10-29 | `build` | Passed | After the ADR-020 restructure, with the one-type-per-file rules at error. |
| 2026-10-29 | `lint` | Passed | |
| 2026-10-29 | `test` | Failed | `S1` and `S5` escalated: Executor's rollback adapter reached the reporting worker instead of the deploy controller. Every other test project passed. |
| 2026-10-29 | Web `lint`, `typecheck`, Playwright | Passed | 41 app tests against the mock API and 112 Storybook accessibility and screenshot tests. |
| 2026-10-30 | Every .NET test project except the end-to-end suites | Passed | 767 tests in 18 projects, including the Testcontainers and Temporal tests. |
| 2026-10-30 | `lint` | Passed | |
| 2026-10-30 | End-to-end `GoldenPathTests` and `FailureScenarioTests` | Passed | `S0` to `S4` in one run, after the adapter fix (22d5661); `S5` in a rerun after its own setup was fixed (b73d7fa). |
| 2026-10-30 | Live `S1` on `muse-spark-1.3` | Passed | Every role on the `meta` profile, after prompt `investigator-v4` and `consensus-v3`: diagnosed, approved, rolled back and recovered, for an estimated $0.24. Two earlier live runs that day escalated and led to those fixes. |
| 2026-10-30 | End-to-end `GoldenPathTests` and `FailureScenarioTests` | Passed | All six scenarios in one run on the final code, with prompt `investigator-v4` and `consensus-v3`, in 43 minutes. Two runs that day failed before any test, when Temporal never started; see Phase 09. |
| 2026-10-30 | Evaluation harness | Passed | 30 harness tests; the [baseline report](../../artifacts/reports/evaluation/report.md) passes its gate. |
| 2026-10-30 | End-to-end `GoldenPathTests`, `FailureScenarioTests` and `ServiceRestartTests` | Passed | All seven tests in one run in 50 minutes, with `consensus-v4`. `S3` was diagnosed correctly as the `order-service` v1.8.0 deployment at confidence 0.60, with a rollback of `order-service` proposed for approval. |
| 2026-10-30 | Every .NET test project except the end-to-end suites | Passed | 809 tests in 18 projects, after the signing key, tool scope, rate-limit and missing-secret changes. |
| 2026-10-30 | End-to-end `S1` and `MessagingOutageTests` (run 14) | Failed | Both on the tests: the quiet check read a missing burn-rate series as healthy, so the outage test, first after start-up, began during the start-up burn and Sentinel rightly abstained on an incident with no change; and the browser gave the postmortem page, which `next dev` compiles on its first visit, only five seconds. Both fixed. |
| 2026-10-30 | End-to-end `S1` with the browser journeys, and `MessagingOutageTests` (run 15) | Passed | Both in 16 minutes. |
| 2026-10-30 | Live `S1`–`S3`, each role on its suggested profile and `changes` on `muse-wonder-1.0` (run 16) | Failed | `S2` and `S3` passed and were diagnosed correctly. `S1` was diagnosed correctly too, but the new browser journey asserted the test double's wording and failed before the approval; it now reads the expected texts from the incident. |
| 2026-10-30 | Live `S1` (runs 17 to 19) | Passed on the third attempt | Run 17: Runtime failed to start while Temporal was running, with no logs to say why; start-up failures now carry the resource's last lines. Run 18: the quiet check never saw a healthy minute, because a minute holds 11 samples, not 12; it now needs 10 and says why when it gives up. Run 19 passed: diagnosed, approved in the browser, rolled back and recovered. The three live scenarios cost $0.99, plus an unrecorded share for run 16's `S1` and any start-up burn investigated in run 18. |
| 2026-10-30 | Reference suite, `ReferenceScenarioTests` (run 20) | Failed | Four of five passed, among them the new trace-continuity test; `S2`'s recovery check regressed inside its observation window. |
| 2026-10-30 | Reference suite, `S1`–`S3` (run 21) | Passed | All three, right after run 20, so `S1` and `S3` ran twice in a row. |
| 2026-10-30 | Floci conformance tests | Passed | Seven gate checks against the pinned 2.3.0 image in 42 seconds, no container left behind ([ADR-021](../decisions/ADR-021-aws-emulator.md)). |
| 2026-10-30 | Evaluation harness | Passed | 35 harness tests; the regenerated [report](../../artifacts/reports/evaluation/report.md) passes its gate, with the test double right on all five incident scenarios and the baseline on three. |
| 2026-10-30 | Full end-to-end regression (run 22) | Invalid, stopped | A configuration edit made during the run reached the running services, which Aspire starts from their project folders: AgentWorker's old binary refused prompt `investigator-v6` and the messaging-outage test timed out. No result of the run counts. |
| 2026-10-30 | End-to-end `S7` and `S8` | `S8` passed; `S7` failed | `S7` abstained at confidence 0.42: the test double's duplicate rule read `S8`'s duplicates, minutes old but inside the lookback, as a rival cause. The queue rules now count only what is under way at onset. |
| 2026-10-30 | End-to-end `S6`, `S7` and `S8` | `S7` and `S8` passed; `S6` failed | `S6` was diagnosed and its message quarantined, but a stall that timed out both Redis instances lost a message in flight for good, so the queue never looked recovered. The consumer now reclaims messages its workers abandon, as SQS's visibility timeout would. |
| 2026-10-30 | Emulator conformance tests | Passed | Ten tests, one class at a time: the seven gate checks, the offline check 9, the AWS environment's lifecycle and the real consumer image on SQS. One rollout check first failed under parallel load and now waits on the tasks themselves. |
| 2026-10-30 | Every .NET test project except the end-to-end suites, after Stage 0b and Stage 1 | Passed | Reference 75, AgentWorker 104 and Runtime 143 among them. |
| 2026-10-30 | End-to-end `AWS-S0` through the AppHost | Passed on the second attempt | The first attempt lost its emulator: the AppHost's cleanup removed containers by a label the emulator itself carried. The second passed in 4 min 42 s, and nothing the emulator started outlived the AppHost. |
| 2026-10-30 | Full end-to-end regression (run 23) | 16 of 17 passed | Every suite in 1 h 47 min: `AWS-S0`, the reference suite, the restart and outage tests, `S1`–`S8`, `S4` and `S5`. `S0` failed on the consumer, not on Sentinel: a receive that failed half-way left its message held with no worker on it, so the reclaim never returned it and the queue aged into an incident. A failed receive now releases its hold. The same review found that `AWS-S0`'s queue-age check had read a series by the wrong name and passed on no data; it now names the series and fails without one. |
| 2026-10-30 | Web `lint`, `typecheck`, Storybook and Playwright after Phase E02 | Passed | 162 Storybook tests with the new baselines; 55 app tests at 1280 px and 768 px, three runs in a row, the hydration fix included. One run of four failed once after taking 21 minutes, which points at a second run reusing the first one's server while it shut down. |
| 2026-10-30 | End-to-end `AWS-S1` | Passed on the fifth attempt | 1: the test asserted the ECS revision among the incident's linked changes, but Runtime links no change that starts serving after onset; it now asserts the revision was proposed and rejected. 2: Docker reported its runtime unhealthy at start; a retry ran. 3: the quarantine moved nothing, because the emulator never changes a redrive policy a queue was created with (ADR-021); the queue now has none. 4: the quarantine worked, but recovery was judged unchanged at a frozen 37.66 s, the last pushed gauge of the task the rollout replaced; queue gauges are now read over 30 s. 5: passed in 7 min 15 s. |
| 2026-10-30 | End-to-end `AWS-S0` | Passed | Twice, beside attempts 1 and 4 of `AWS-S1`, each in 4 min 44 s. |
| 2026-10-31 | Ingestion, Executor and reference-system tests after Stage 2 | Passed | Ingestion 98 with its emulator tests and 44 conformance cases, three AWS adapters among them; Executor 75, and its three SQS quarantine tests three times in a row; Reference 79, the Floci compatibility checks and the AWS environment tests. |
| 2026-10-31 | Evaluation harness and report | Passed | 40 harness tests. The report, regenerated on dataset `scenarios-v2`, scores all ten packages and passes its gate; in `S6`, `S7`, `S8` and `AWS-S1` the test double is right and the baseline would roll back the innocent consumer. |
| 2026-10-31 | Web `lint`, `typecheck` and Playwright | Passed | 56 app tests twice in a row with the quarantine journey. The tablet pass now runs after the desktop one: both share the stateful mock API, and a reset in either could undo a step of the other, which explains the earlier unexplained failure. |

## Phase 00 – Foundations

Status: **In progress**.

Delivered:

- Five services, each with Api, Application, Domain and Infrastructure projects, and the web app: [services](../../services/), [apps/Sentinel.Web](../../apps/Sentinel.Web/), [Sentinel.slnx](../../Sentinel.slnx); the SDK and packages are pinned in [global.json](../../global.json) and [Directory.Packages.props](../../Directory.Packages.props).
- Building blocks layered into packages with a changelog and API baselines: [CleanArchitecture.BuildingBlocks](../../packages/CleanArchitecture.BuildingBlocks/README.md), [CHANGELOG](../../packages/CleanArchitecture.BuildingBlocks/CHANGELOG.md), [ADR-013](../decisions/ADR-013-building-block-reuse.md). The messaging acceptance test (one crossing, a redelivered duplicate suppressed) is [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Typed identifiers and schema-versioned contracts with snapshots of the whole lifecycle: [identifiers](../../packages/Sentinel.Contracts/src/Sentinel.Contracts.Primitives/Identifiers/), [ContractSnapshotTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractSnapshotTests.cs), [ContractRulesTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractRulesTests.cs).
- Provider and model interfaces inside their owning services, alerts included ([IAlertProvider](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Abstractions/Providers/IAlertProvider.cs)), with a deterministic test double: [Ingestion providers](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Abstractions/Providers/), [IModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Abstractions/Providers/IModelProvider.cs), [DeterministicModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/DeterministicModelProvider.cs), [ADR-014](../decisions/ADR-014-provider-abstraction.md), [ADR-015](../decisions/ADR-015-model-abstraction.md).
- Architecture tests in every service enforcing layer direction, service isolation and ADR-020 rules 6 to 13, for example [Runtime's](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Architecture.Tests/LayerDependencyTests.cs). Isolation is checked on project references as well as on types, so a reference to another service fails even before any type uses it: [ProjectReferenceRuleTests](../../packages/Sentinel.Testing/tests/Sentinel.Testing.Tests/ProjectReferenceRuleTests.cs).
- Local bootstrap, health, OpenTelemetry and generated secret parameters: [AppHost](../../deploy/local/Sentinel.AppHost/AppHost.cs), [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), [HealthEndpointTests](../../packages/Sentinel.ServiceDefaults/tests/Sentinel.ServiceDefaults.Tests/HealthEndpointTests.cs).
- ADR-001 to ADR-020, with ADR-020 still Proposed. Templates: [ADR](../decisions/TEMPLATE.md), [schema change](../business-contracts/schema-change-template.md).
- Design system with tokens, themes, stories, accessibility and screenshot checks: [design-system](../../apps/Sentinel.Web/src/design-system/), [storybook.spec.ts](../../apps/Sentinel.Web/tests/visual/storybook.spec.ts) with baselines in [tests/__screenshots__](../../apps/Sentinel.Web/tests/__screenshots__/), [check-design-system.mjs](../../apps/Sentinel.Web/scripts/check-design-system.mjs).
- Repeatable tasks: [scripts/check.ps1](../../scripts/check.ps1), [PROJECT.json](../../PROJECT.json).
- Startup refuses a missing or blank secret, each tested: Executor's deploy token, without which it does not start, and its reporting control token, without which pause-batch has no adapter ([AdapterRegistrationTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/AdapterRegistrationTests.cs)); Ingestion's deploy controller token ([ProviderRegistrationTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderRegistrationTests.cs)); the deploy controller's own tokens ([DeployControllerConfigurationTests](../../reference-system/tests/Reference.Tests/DeployControllerConfigurationTests.cs)); and, as before, the model credentials and the reference control token.
- The tenant, environment, service and incident on every consumer span and in the log scope of every message handler, and on incident requests at the Gateway, under the names in [SentinelTags](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/SentinelTags.cs): [MessageContextTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.Tests/MessageContextTests.cs), [TenantScopeTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/TenantScopeTests.cs).

Open:

- A CI pipeline. None exists, so the design-system and package rules that require checks in CI are open too.

## Phase 01 – Reference system

Status: **Complete**. The reference suite passed on 2026-10-30, `S1`–`S3` in two runs one after the other ([Recorded runs](#recorded-runs)).

Delivered:

- Checkout, orders, the reporting worker and the Redis exporter, with healthy and faulty revisions and each fault in its own file: [Reference.Checkout](../../reference-system/services/Reference.Checkout/), [Reference.Orders](../../reference-system/services/Reference.Orders/), [Reference.ReportingWorker](../../reference-system/services/Reference.ReportingWorker/), [RedisBehaviourTests](../../reference-system/tests/Reference.Tests/RedisBehaviourTests.cs), [RevisionBehaviourTests](../../reference-system/tests/Reference.Tests/RevisionBehaviourTests.cs), [ADR-011](../decisions/ADR-011-reference-system-location.md).
- The deploy controller with its ledger, and the scenario runner with traffic, reset and timeline execution: [DeploymentLedger](../../reference-system/deploy-controller/Reference.DeployController.Domain/Revisions/DeploymentLedger.cs), [ScenarioRunner](../../reference-system/scenarios/Reference.Scenarios.Application/Runs/ScenarioRunner.cs), [DeploymentLedgerTests](../../reference-system/tests/Reference.Tests/DeploymentLedgerTests.cs), [DeployControllerEndpointTests](../../reference-system/tests/Reference.Tests/DeployControllerEndpointTests.cs), [ScenarioRunnerEndpointTests](../../reference-system/tests/Reference.Tests/ScenarioRunnerEndpointTests.cs).
- Failure controls exposed only when enabled, and guarded by a token: [FailureControl](../../reference-system/services/Reference.Common/Control/FailureControl.cs).
- Manifests `S0`–`S3` with ground truth, discriminating signals and recovery, and the baseline definition: [manifests](../../reference-system/scenarios/manifests/), [ScenarioManifestTests](../../reference-system/tests/Reference.Tests/ScenarioManifestTests.cs), [deploy-blame.md](../../reference-system/scenarios/baseline/deploy-blame.md).
- Symptom, signal and recovery checks for `S1`–`S3` across the real stack, and a checkout traced as one trace across checkout, orders and Redis: [ReferenceScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/ReferenceScenarioTests.cs).

Caveats, not exit criteria:

- `ReferenceScenarioTests` does not run `S0` as a scenario; its healthy-system test checks the same SLO without the runner.
- `S2`'s recovery check regressed once inside its observation window, in run 20, and passed in run 21; the check now names the criterion and value that missed, for the next occurrence.

## Phase 01A – Walking skeleton

Status: **In progress**.

Delivered:

- The golden path end to end. `S1` runs from the break to approval in the browser, a single rollback and verified recovery; `S2` and `S3` run through decision and plan; `S0` opens no incident: [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs).
- Approval signed in the Gateway and verified in Executor, with negative tests: [ApprovePlanCommandHandler](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/BusinessTasks/Commands/Approvals/ApprovePlanCommandHandler.cs), [LocalApprovalSigner](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Infrastructure/Security/LocalApprovalSigner.cs), [AttestationVerifier](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/Services/AttestationVerifier.cs), [AttestationTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/AttestationTests.cs), [ApprovalEndpointTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/ApprovalEndpointTests.cs).
- The deploy-blame baseline running in shadow: [DeployBlameInvestigator](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/DeployBlameInvestigator.cs).
- Single-service restarts repeat nothing: Runtime and Executor restart while the golden plan awaits approval, and the approval still ends in one rollback and a verified recovery, with one incident and one plan; it passed on 2026-10-30: [ServiceRestartTests](../../deploy/local/Sentinel.EndToEnd.Tests/ServiceRestartTests.cs).
- Idempotent handling of duplicate delivery: [RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs), [InboundMessageProcessorTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.Tests/InboundMessageProcessorTests.cs).
- The comparison report, with `S1`, `S2` and `S3` records: [comparison.md](../../artifacts/reports/walking-skeleton/comparison.md). `S1` ran from the break through approval in the browser to a verified rollback and recovery on 2026-10-30: [S1.outcome.json](../../artifacts/reports/walking-skeleton/S1.outcome.json).
- A live run on `muse-spark-1.3`, with every role on the `meta` profile, documented with its agent executions: `S1` diagnosed the v2.4.1 deployment with confidence 0.87, proposed the rollback, and recovered after approval, for an estimated $0.24 ([S1.meta.json](../../artifacts/reports/evaluation/runs/S1.meta.json)). Two earlier live runs that day exposed the defects fixed by prompt `investigator-v4` (71db520) and `consensus-v3` (fcccfce); an earlier live `S2` run on 2026-10-29 produced no findings, and its record was overwritten before the harness kept live runs apart (9c2dd19).

Open:

- The deterministic CI run, and the end-to-end suite as a required CI check.

## Phase 02 – Telemetry ingestion

Status: **Complete**. Every exit criterion has evidence, and the golden scenario produced queryable observations of every kind end to end on 2026-10-30.

Delivered:

- Prometheus, Loki, Jaeger, deploy-controller and runbook adapters with conformance tests: [Providers](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/), [ProviderConformanceTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderConformanceTests.cs), [SignalAdapterTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/SignalAdapterTests.cs).
- Checkpoints, backoff, late data and deterministic IDs: [IngestionCycle](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Services/IngestionCycle.cs), [SourceState](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Domain/Sources/SourceState.cs), [IngestionCycleTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/IngestionCycleTests.cs), [SourceStateTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/Domain/SourceStateTests.cs), [ADR-016](../decisions/ADR-016-event-time-and-late-data.md).
- Redaction before storage: [RedactionPolicy](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Domain/Observations/RedactionPolicy.cs), [RedactionTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/RedactionTests.cs).
- The query contract with trust metadata, and the query bulkhead: [QueryObservationsQueryHandler](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/Observations/QueryObservationsQueryHandler.cs), [QueryBulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/QueryBulkhead.cs), [IngestionEndpointTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/IngestionEndpointTests.cs).
- Prioritized pull loops, bounded provider clients and ingestion telemetry: [IngestionWorker](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Workers/IngestionWorker.cs), [IngestionTelemetry](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Diagnostics/IngestionTelemetry.cs), [ADR-018](../decisions/ADR-018-observation-publication-scope.md).
- An operational view of the sources on the control plane: each source's health, how far its data is complete and how far that trailed its last pull, and for a failing source its failures in a row, its next try after backoff and its last error, failing sources first: [source-health.tsx](../../apps/Sentinel.Web/src/features/admin/source-health.tsx), [SourceSummary](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessProcesses/Sources/SourceSummary.cs).
- Dead letters, with operator list and replay endpoints: [OperatorEndpoints](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/OperatorEndpoints.cs). Replay after the cause is fixed is tested: [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Paging through continuation tokens against PostgreSQL: [PostgresObservationStoreTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/PostgresObservationStoreTests.cs).
- A compatibility snapshot of the query answer, which pins the log, trace, deployment, topology and alert observations: [ingestion.query.observations.result.json](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/Snapshots/ingestion.query.observations.result.json).
- Alerts as a signal type: the reference Prometheus raises `CheckoutSloFastBurn` and `RedisClientsNearLimit`, Ingestion reads the pending and firing alerts on every poll as [AlertObservation](../../packages/Sentinel.Contracts/src/Sentinel.Contracts/Observations/AlertObservation.cs)s, with each state its own observation and annotations redacted, and answers alert queries. Alerts are stored and queried but not published, as [ADR-018](../decisions/ADR-018-observation-publication-scope.md) publishes only kinds with an event consumer: [PrometheusAlertsProvider](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/PrometheusAlertsProvider.cs), [ProviderConformanceTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderConformanceTests.cs), [slo.yml](../../reference-system/deploy/prometheus/rules/slo.yml).

## Phase 03 – Incident runtime

Status: **Complete**. Every exit criterion has a test, and the Runtime suite, including the ADR-005 test of Runtime and Temporal failing together, passed on 2026-10-30 ([Recorded runs](#recorded-runs)); `S0` and `S1` passed end to end.

Delivered:

- The incident aggregate with its child lifecycles and state tables: [Incident](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Incident.cs), [IncidentStateMachine](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/IncidentStateMachine.cs), [IncidentStateMachineTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentStateMachineTests.cs).
- Correlation with absorb, merge, open, reopen and suppress decisions: [CorrelationEngine](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Correlation/CorrelationEngine.cs), [CorrelationEngineTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/CorrelationEngineTests.cs).
- Grains as single writers, with restart and silo-loss tests: [Grains](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Grains/), [IncidentGrainClusterTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentGrainClusterTests.cs).
- The event store with optimistic concurrency, projections and rebuild: [PostgresIncidentStore](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Persistence/PostgresIncidentStore.cs), [IncidentProjector](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Projections/IncidentProjector.cs).
- The Temporal workflow, with the ADR-005 acceptance tests run on a real server: a worker killed mid-run, a versioned upgrade, a server restart, Runtime and Temporal failing together, backup and restore, and duplicate, rejected and expired approvals. See [IncidentWorkflow](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs) and [IncidentWorkflowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentWorkflowTests.cs).
- An `OutcomeUnknown` execution result waits up to `ReconciliationTimeout` for Executor's reconciled result instead of escalating at once (review finding F-002), behind a workflow patch so runs started earlier replay unchanged: [IncidentWorkflow](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs), [WorkflowSetting](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/ControlPlane/WorkflowSetting.cs).
- Behaviour tests in [RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs): one incident for the golden burst, none for `S0`, duplicate and concurrent commands, and deleted then rebuilt projections.
- The event store and projections against PostgreSQL: stream order and payloads, one writer per version under concurrent appends, and the open-incident and correlation queries: [PostgresIncidentStoreTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/PostgresIncidentStoreTests.cs).

Open, not exit criteria:

- A correlation split rule.

## Phase 04 – AI investigation

Status: **Complete**. Every exit criterion has evidence, and the live runs on 2026-10-30 produced validated findings from every telemetry specialist, with competing hypotheses; the knowledge specialist rightly abstained because runbook passages cannot be cited, and a traces answer that cited an observation no tool returned was refused.

Delivered:

- Six specialists and a generalist, each limited to its role's tools, inside an execution with token, tool and repair budgets: [Specialists](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/Specialists.cs), [Tools](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Tools/), [AgentExecution](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Domain/Executions/AgentExecution.cs), [SpecialistTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/SpecialistTests.cs), [AgentExecutionTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/Domain/AgentExecutionTests.cs).
- The Meta adapter, the test double, per-role routing and a versioned prompt: [MetaModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/MetaModelProvider.cs), [ModelRouter](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/ModelRouter.cs), [InvestigatorPrompt](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/InvestigatorPrompt.cs).
- Meta models configured by profile, each a model, reasoning effort, reasoning-token allowance and list prices; findings requested with their JSON Schema alongside the tools; every call costed, with cached and reasoning tokens; and spend caps in total, per day and per model, at which investigations abstain and say why: [MetaModelProfile](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/MetaModelProfile.cs), [ModelSpendGuard](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/ModelSpendGuard.cs), [MetaModelProviderTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/MetaModelProviderTests.cs), [ModelSpendTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/ModelSpendTests.cs), [models and spend runbook](../runbooks/models-and-spend.md).
- An incident's specialists run side by side, because the stream consumer handles a batch concurrently: [RedisStreamsConsumer](../../packages/CleanArchitecture.BuildingBlocks/src/CleanArchitecture.BuildingBlocks.Infrastructure.Redis/RedisStreamsConsumer.cs).
- Findings validation, tested in [FindingsValidatorTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/FindingsValidatorTests.cs) and [InvestigationTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/InvestigationTests.cs):
  - claims cite observation IDs and tool calls;
  - competing hypotheses are required;
  - invalid output abstains;
  - proposed actions are refused.
- The hypothesis blackboard and causal graph: [HypothesisBlackboard](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/HypothesisBlackboard.cs), [BlackboardTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/BlackboardTests.cs).
- Prompt-injection fixtures in a runbook and in a log line, each refused when a model obeys it: [Telemetry.cs](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/Telemetry.cs), [SpecialistTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/SpecialistTests.cs), [reporting-worker.md](../../reference-system/knowledge/reporting-worker.md).
- Model telemetry for tokens, latency, cost, refused calls and rejected outputs, each tested: [AgentWorkerTelemetry](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Diagnostics/AgentWorkerTelemetry.cs), [SpecialistTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/SpecialistTests.cs).

- Tenant isolation in tools: every tool queries the investigation's tenant, environment and window, even when the model names another tenant, environment or a wider window in its arguments: [ToolScopeTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/ToolScopeTests.cs).
- Rate limiting: the Meta adapter reports a 429 as rate limited without retrying, and the investigation fails after that one call and says why, so a rate limit costs nothing more and Runtime decides with the roles that answered: [MetaModelProviderTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/MetaModelProviderTests.cs), [InvestigationTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/InvestigationTests.cs).

## Phase 05 – Verification and consensus

Status: **Complete**. Every exit criterion has evidence, and on 2026-10-30 Sentinel diagnosed `S1`, `S2` and `S3` correctly end to end with no false remediation, where the baseline remediated falsely on `S2` and `S3`.

Delivered:

- A deterministic adversarial verifier (`falsifiers-v2`) in AgentWorker that challenges hypotheses and proposes alternatives. Version 2 falsifies a change that was replaced before the symptoms began, and measures a change's mechanism from just before it was served, so an earlier fault cannot pass as its effect: [AdversarialVerifier](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/AdversarialVerifier.cs), [VerifierTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/VerifierTests.cs).
- `consensus-v4` with independence families, contradiction weighting, dissent, abstention and an inputs hash. Version 2 stopped counting the leading change's own mechanism, such as Redis saturation in the changed service, as its rival. Version 3 also lets a standing change claim the saturation investigators blamed on its own service, so the saturation's evidence counts for the change even when the saturation scored higher alone, as it did in the first live run with findings. Version 4 lets a change claim the regression of its own service in the same way, which had left `S3` abstaining with its true cause in the lead. Each decision replays by the version it was made with: [Consensus](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Consensus.cs), [ConsensusSettings](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/ConsensusSettings.cs), [ConsensusTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/ConsensusTests.cs).
- Calibration and source reliability measured by the evaluation harness: per investigator, the Brier score, a reliability table by stated confidence and the expected calibration error over its diagnoses, reported as calibrated only from 30 scored diagnoses with an error of at most 0.1, and per provider the share of evidence that matched the ground truth. With six scenarios Sentinel stays reported as uncalibrated, and the table shows the test double states less confidence than its accuracy earns: [calibration.py](../../scripts/evaluation/sentinel_eval/calibration.py), [test_evaluation.py](../../scripts/evaluation/tests/test_evaluation.py), [report](../../artifacts/reports/evaluation/report.md).
- Confidence and hypothesis comparison in the web app: [confidence-display](../../apps/Sentinel.Web/src/design-system/domain/confidence-display.tsx), [hypothesis-comparison](../../apps/Sentinel.Web/src/design-system/patterns/hypothesis-comparison.tsx).

- Sentinel beats the deploy-blame baseline on `S1`–`S3`. With the test double it blames the faulty deployment on `S1`, the reporting batch on `S2`, where it never proposes a `checkout-service` rollback, and the `order-service` v1.8.0 deployment on `S3`, with zero false remediations; the baseline blames `checkout-service` every time, so it remediates falsely on `S2` and `S3`. The live model also diagnosed `S1` correctly: [report](../../artifacts/reports/evaluation/report.md), [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs).
- The same on Meta's models, on 2026-10-30, with each role on its suggested profile and `changes` on `muse-wonder-1.0`: `S1` blamed on the `checkout-service` v2.4.1 deployment at confidence 0.89, approved, rolled back and recovered; `S2` on the reporting worker's Redis saturation at 0.89, escalated without a rollback; `S3` on the `order-service` v1.8.0 deployment at 0.78, with its rollback awaiting approval. No false remediation, evidence recall 96%, and $0.99 for the three runs: [report](../../artifacts/reports/evaluation/report.md), [runs](../../artifacts/reports/evaluation/runs/).

## Phase 06 – Safe remediation

Status: **Complete**. Every exit criterion has evidence, and `S1` and `S5` passed end to end on 2026-10-30.

Delivered:

- Policy as code with a rule trace, evaluating emergency stop and disabled actions first, then rules by environment, target, risk, confidence, blast radius, severity and freezes: [PolicyEngine](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Policy/PolicyEngine.cs), [remediation-policy.json](../../services/Sentinel.Executor/src/Sentinel.Executor.Api/policies/remediation-policy.json), [PolicyTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/PolicyTests.cs).
- Four autonomy modes, applied as a per-environment ceiling: [AutonomyMode](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Policy/AutonomyMode.cs).
- Single-use capabilities with scope, limits and expiry: [Capability](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Capabilities/Capability.cs), [ExecutionTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/ExecutionTests.cs).
- Signing key rotation, and the approval aggregate with revocation and expiry: [LocalApprovalSignerTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/LocalApprovalSignerTests.cs), [Approval](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Domain/Approvals/Approval.cs), [ApprovalTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/Domain/ApprovalTests.cs), [RemediationLifecycleTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RemediationLifecycleTests.cs).
- The execution state machine with fencing, start confirmation, `OutcomeUnknown` reconciliation and compensation in reverse order: [ExecutionRequest](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Executions/ExecutionRequest.cs), [ReconciliationWorker](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Execution/ReconciliationWorker.cs). Reconciliation leaves alone a request this process is still handling, however long a step takes: [InFlightExecutions](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/Services/InFlightExecutions.cs). A target that cannot be inspected refuses the request with the reason, and each adapter has its own HTTP client, so the rollback adapter reaches the deploy controller: [AdapterRegistrationTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/AdapterRegistrationTests.cs).
- Tests for a plan superseded after approval, recovery judged without an incomplete baseline, and Executor crashes in the `Received` and `StartConfirmed` states: [RemediationLifecycleTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RemediationLifecycleTests.cs), [RecoveryAssessorTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RecoveryAssessorTests.cs), [ExecutionTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/ExecutionTests.cs).
- Signed decisions and operator attestations ([ADR-006](../decisions/ADR-006-remediation-control-path.md) amendment):
  - the Gateway signs approvals, rejections and revocations, and Runtime verifies each before acting on it: [DecisionVerifier](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Services/DecisionVerifier.cs), [DecisionVerifierTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/DecisionVerifierTests.cs);
  - Executor authenticates an execute command before it binds the idempotency key, and accepts admin changes only with an operator attestation: [ExecuteRemediationCommandHandler](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/BusinessTasks/Commands/Execution/ExecuteRemediationCommandHandler.cs), [OperatorAttestationFilter](../../services/Sentinel.Executor/src/Sentinel.Executor.Api/Filters/OperatorAttestationFilter.cs);
  - verification keys refresh, retire when the Gateway stops publishing them and are not trusted when stale: [ApiApprovalKeysTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/ApiApprovalKeysTests.cs);
  - revocation is answered synchronously by Runtime: [RemediationController](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Api/Controllers/RemediationController.cs).
- Policy versions published by compare and append, and every control change audited in the same transaction: [VersionedPolicyStoreTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/VersionedPolicyStoreTests.cs), [PostgresControlStoreTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/PostgresControlStoreTests.cs).
- Rollback and batch adapters: [DeployControllerRollbackAdapter](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Remediation/DeployControllerRollbackAdapter.cs), [ReportingBatchAdapter](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Remediation/ReportingBatchAdapter.cs).
- Recovery judged against the pre-action baseline: [RecoveryAssessor](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Recovery/RecoveryAssessor.cs), [RecoveryCriteriaTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RecoveryCriteriaTests.cs).
- End-to-end tests of the policy result and a single rollback on the golden path, and of a failed rollback escalated without retry: [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs), [FailureScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/FailureScenarioTests.cs).

Remaining scope, not an exit criterion:

- A managed, non-exportable signing facility. Only the local signer exists, and choosing one waits on the production deployment environment.

## Phase 07 – Memory, replay and evaluation

Status: **Complete**.

Delivered:

- Incident memory with a version manifest and retention metadata ([ADR-019](../decisions/ADR-019-incident-memory-replay-and-retention.md)): [IncidentMemoryBuilder](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Memory/IncidentMemoryBuilder.cs), [PostgresIncidentMemoryStore](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Persistence/PostgresIncidentMemoryStore.cs), [MemoryReplayTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/MemoryReplayTests.cs).
- Replay at three levels, all reaching responders through the Gateway:
  - Level 1, domain: [DomainReplay](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Memory/DomainReplay.cs).
  - Levels 2 and 3, investigation and model reevaluation, from captured inputs only: [ReplayInvestigationCommandHandler](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/BusinessTasks/Commands/Replays/ReplayInvestigationCommandHandler.cs), [ReplayTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/ReplayTests.cs).
- Postmortems whose facts cite their sources, with a versioned narrative: [PostmortemBuilder](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Memory/PostmortemBuilder.cs), [SaveNarrativeCommandHandler](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/BusinessTasks/Commands/Postmortems/SaveNarrativeCommandHandler.cs).
- Similar-incident retrieval within the tenant and its retention, with provenance: [IncidentSimilarity](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Memory/IncidentSimilarity.cs).
- Retention, legal hold, privacy deletion, and a purge that runs as a dry run first: [memory commands](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/BusinessTasks/Commands/Memory/).
- Event upcasting for streams written before Phase 07: [IncidentEventUpcasters](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Persistence/IncidentEventUpcasters.cs).
- Scenario packages `S0`–`S5` and a versioned dataset: [scenario packages](../../reference-system/scenarios/packages/), [dataset.json](../../scripts/evaluation/dataset.json).
- The evaluation harness, with a promotion gate against the baseline: [evaluate.py](../../scripts/evaluation/evaluate.py), [scoring.py](../../scripts/evaluation/sentinel_eval/scoring.py), [test_evaluation.py](../../scripts/evaluation/tests/test_evaluation.py). The run writer is [EvaluationRun](../../deploy/local/Sentinel.EndToEnd.Tests/EvaluationRun.cs).

Exit criteria with test evidence: the event stream rebuilds the same state, postmortem facts trace to their sources, and similar-incident retrieval respects tenant and retention boundaries ([MemoryReplayTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/MemoryReplayTests.cs)).

Exit criteria met on 2026-10-30:

- The baseline evaluation report, from schema-2 runs of every scenario in the dataset, with accuracy, evidence, safety, latency and cost beside the deploy-blame baseline: [report.md](../../artifacts/reports/evaluation/report.md), [report.json](../../artifacts/reports/evaluation/report.json), [runs](../../artifacts/reports/evaluation/runs/).
- The golden incident replayed without live providers or actions: the `S1` test ends by replaying it through the Gateway, at level 1 from its stream and at level 2 from each investigation's captured inputs, and it passed ([GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs)).

Delivered from the test plan and scope:

- Captured inputs matched by a canonical hash, and stable version manifests: [CaptureStabilityTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/CaptureStabilityTests.cs), [MemoryReplayTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/MemoryReplayTests.cs).
- The retention SQL tested against PostgreSQL: [PostgresIncidentMemoryStoreTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/PostgresIncidentMemoryStoreTests.cs).
- Consensus accuracy and hallucinated evidence as named measures, and cost from each run's recorded estimate: [scoring.py](../../scripts/evaluation/sentinel_eval/scoring.py), [test_evaluation.py](../../scripts/evaluation/tests/test_evaluation.py).

Open from the test plan and scope, not exit criteria:

- A regression-kind scenario package; the schema allows the kind, but no package uses it.
- The simulation interface, with the reference system as its first implementation. Only the level 4 enum value exists. The phase defers a full digital twin deliberately.

## Phase 08 – Product experience

Status: **In progress**.

Delivered:

- Live updates over server-sent events, resumable, with reconnection and a polling fallback in the web app: [LiveController](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Controllers/LiveController.cs), [live-updates.tsx](../../apps/Sentinel.Web/src/features/incidents/live-updates.tsx).
- JWT bearer identity with three roles, a development identity available only in Development, and tenant scoping: [IdentityExtensions](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Identity/IdentityExtensions.cs), [IdentityTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/IdentityTests.cs), [IncidentTenantFilter](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Filters/IncidentTenantFilter.cs).
- Idempotency keys on mutations, and paginated timeline and evidence: [IdempotencyMiddleware](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Idempotency/IdempotencyMiddleware.cs), [Pages](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Incidents/Pages.cs).
- The control-plane API with audit, and each service's configuration document: [AdminController](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Controllers/AdminController.cs), [ControlPlaneAudit](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/ControlPlane/ControlPlaneAudit.cs), [GatewayEndpointTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/GatewayEndpointTests.cs).
- Notification routing and privacy-safe product analytics: [NotificationRouter](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Live/NotificationRouter.cs), [AnalyticsCatalog](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Analytics/AnalyticsCatalog.cs).
- Responder, postmortem and admin pages, tested in [incidents.spec.ts](../../apps/Sentinel.Web/tests/e2e/incidents.spec.ts): [incident-detail.tsx](../../apps/Sentinel.Web/src/features/incidents/incident-detail.tsx), [postmortem page](../../apps/Sentinel.Web/src/app/incidents/[incidentId]/postmortem/page.tsx), [admin page](../../apps/Sentinel.Web/src/app/admin/page.tsx).
- The control plane in the web app: an overview with the emergency stop, a manual override per action kind, the audit history and usage counts; the [policy page](../../apps/Sentinel.Web/src/app/admin/policy/page.tsx); and a [page per configuration area](../../apps/Sentinel.Web/src/app/admin/configuration/[area]/page.tsx) that shows the current version and where it came from, publishes over the version it was read at, and lists every version. The web tests cover publishing, refusals for a stale or unchanged version, overrides and role refusal.
- The responder journeys against the real system, in `S1`: before approving, the responder reviews the correlated change, the evidence, the competing hypotheses and the decision's checks in Sentinel.Web; after the recovery, the execution and recovery verdicts, the postmortem's decision, a saved narrative and a replay that reproduces the incident. It passed on 2026-10-30: [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs).
- Product analytics sent from the web app for every event in the catalog, honouring Global Privacy Control and Do Not Track: [track.ts](../../apps/Sentinel.Web/src/features/analytics/track.ts), [product-analytics.md](../business-contracts/product-analytics.md).
- Long incidents stay usable: the page sends only the latest 50 timeline entries to the browser and pages older ones in from the Gateway's paged timeline, evidence renders 50 rows at a time, and a link to evidence further down reveals it. A test with 5,000 timeline entries and 400 evidence items in the causal graph holds the page to its time budget: [timeline-list.tsx](../../apps/Sentinel.Web/src/features/incidents/timeline-list.tsx), [evidence-table.tsx](../../apps/Sentinel.Web/src/features/incidents/evidence-table.tsx), [incidents.spec.ts](../../apps/Sentinel.Web/tests/e2e/incidents.spec.ts).
- The external API documented with its conventions, idempotency, paging, the live stream and the control plane: [synchronous contracts](../business-contracts/synchronous-contracts.md#the-gateways-external-http-api).

Open:

- The administrator journey against the real system. Sentinel.Web acts as one development identity, a responder and approver, so control-plane changes are tested through the Gateway's API and against the mock API, not through the real UI; a second identity in one run arrives with [ADR-023](../decisions/ADR-023-approver-identity.md).
- A wired identity provider. [ADR-023](../decisions/ADR-023-approver-identity.md) proposes OpenID Connect sign-in in Sentinel.Web, with the tokens kept on its server and a recent multi-factor sign-in required to approve, and awaits a decision.
- Structured editors for configuration areas. Each area is edited as its JSON document, which the owning service validates.

## Phase 09 – Production hardening

Status: **In progress**.

Delivered:

- Resilience and health in [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), the [query bulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/QueryBulkhead.cs), and the Temporal restart and backup tests.
- Runbooks for the local environment: emergency stop, models and spend, dead letters, projection rebuild, signing key rotation, and backup and restore ([runbooks](../runbooks/README.md)). A Gateway restart now retires the signing key it replaces once that key's attestations expire, instead of leaving it published: [LocalApprovalSignerTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/LocalApprovalSignerTests.cs).
- A threat model with the trust boundaries, the open risks and the status of every review finding: [threat-model.md](../architecture/threat-model.md). Three findings are open: F-007 and F-013 wait on service-to-service authentication, and F-011 on a model-result cache.
- End-to-end failure scenarios: the golden incident with the logs provider down (`S4`) and with a rollback that fails (`S5`): [FailureScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/FailureScenarioTests.cs).
- The message broker frozen for 45 seconds while the golden plan is approved, as a partition would freeze it: the Gateway accepts the approval through its outbox, and the incident ends in one rollback, one recorded approval and one incident. It passed on 2026-10-30: [MessagingOutageTests](../../deploy/local/Sentinel.EndToEnd.Tests/MessagingOutageTests.cs). A consumer whose group the broker lost creates it again, in `CleanArchitecture.BuildingBlocks` `alpha.9`: [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Sentinel's own SLOs for freshness, detection, decision time, event delivery and availability, each measured by a metric the services export, with guardrails that have no error budget: [service-level objectives](../architecture/service-level-objectives.md). Runtime emits the incident timings and outcomes they need: [RuntimeTelemetry](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Diagnostics/RuntimeTelemetry.cs), [RuntimeTelemetryTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeTelemetryTests.cs).

Open:

- A deployment definition beyond `deploy/local/`, which waits on the choice of production environment.
- Service-to-service authentication. [ADR-022](../decisions/ADR-022-service-to-service-authentication.md) proposes signed service tokens with a caller allowlist per route and a Redis user per service, and awaits a decision. Until then Runtime's operator endpoints stay unauthenticated ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)).
- Alerting on the SLOs. They are defined and measured, but nothing pages anyone yet; that arrives with the deployment definition.
- Failure injection beyond `S4`, `S5`, single-service restarts and a frozen broker, such as a slow database or a broker that restarts without its data.
- A deployment runbook, and production versions of key rotation and restore, which arrive with the deployment definition.
- A whole-system restore drill: every database restored together with an incident in flight, which then completes. Temporal's and Runtime's databases are each tested restored into a new server ([IncidentWorkflowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentWorkflowTests.cs), [RuntimeRestoreTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeRestoreTests.cs)), but not every database together, so the [backup and restore](../runbooks/backup-and-restore.md) steps are designed rather than proven.
- The local stack sometimes fails to start: in two of thirteen end-to-end runs on 2026-10-29 and 30 the Temporal container was never created, and in run 17 Runtime failed to start although Temporal was running. A rerun succeeded each time, and the cause is not yet known. A start-up failure now reports the resource's and Temporal's last log lines, which the next occurrence should be read from.

## Phase 10 – Scale and evolution

Status: **Not started**. Requires measured need and an ADR per item.

## Phase E01 – AWS-emulated reference environment

Status: **In progress**. The first enhancement phase ([plan](README.md#enhancement-phases), [phase document](phase-e01-aws-emulated-reference-environment.md)). Its design source, the [AWS-emulated reference environment proposal](../architecture/aws-emulated-reference-environment.md), is Proposed as of 2026-10-30.

Delivered:

- Stage 0, decision evidence, on 2026-10-30. The owner chose Floci over LocalStack and MiniStack, and [ADR-021](../decisions/ADR-021-aws-emulator.md) records it, pinned to 2.3.0 by digest in [emulator.json](../../reference-system/deploy/aws/emulator.json), with the verified operation matrix, the known incompatibilities, the resource figures and the Docker-socket constraint. The matrix is the result of seven environment conformance tests that start the pinned image and exercise SQS with a dead-letter queue, an ECS service rolling out a real container, IAM and STS, account and region isolation, CloudWatch Logs and Metrics, and a reset that leaves no container behind; all pass: [Reference.Tests/Aws](../../reference-system/tests/Reference.Tests/Aws/). They carry the `Emulator` trait, which the default `test` task excludes.
- The [ADR-011 amendment](../decisions/ADR-011-reference-system-location.md#aws-emulated-environment-amendment-2026-10-30) for the layout of the second environment.
- Stage 0b, the queue track in the existing reference system, on 2026-10-30. A queue-consumer with a queue of its own and three scenarios in which the latest deployment is innocent and the deploy-blame baseline would roll the consumer back:
  - `S6`, a poison message: Sentinel diagnoses it, proposes the new `quarantine-message` action, which policy `policy-2026.10.3` sends for approval, quarantines it through Executor's queue adapter and verifies that the queue recovers.
  - `S7`, retry amplification: order-service's confirmations slow down and the consumer's immediate retries amplify it. Sentinel blames order-service, not the consumer, and escalates, because no action it holds contains the retries.
  - `S8`, duplicate delivery: orders published twice meet a booking that is not idempotent. Sentinel diagnoses `duplicate-delivery:queue-consumer` and escalates without a plan.
  Prompt `investigator-v7` names the three causes; Ingestion adds the queue series; Runtime detects the queue's age and duplicate rate and plans the quarantine. Each scenario has an evaluation package, a baseline row and an end-to-end test, and the consumer has a runbook in the knowledge base. Two defects surfaced on the way and are fixed: Runtime cleared an incident once any of its service's detection rules had been quiet for two minutes, not only the rule that opened it, and the Redis queue lost a message whose receive reply or settle failed.
- Stage 1, the healthy AWS environment, on 2026-10-30. `Sentinel:ReferenceEnvironment=aws` starts the pinned emulator as `aws-emulator` and publishes the consumer's image; the scenario runner drives the environment through `IReferenceEnvironment`, with the local and AWS implementations in the new `Reference.Scenarios.Infrastructure`, and provisions the SQS queue, its dead-letter queue and the consumer's ECS service through the public APIs. The consumer runs there on SQS and sends its telemetry to the same Prometheus, Loki and Jaeger; manifests name their environment, account and region, and a runner refuses another environment's scenario. `AWS-S0` passed through the AppHost, and gate check 9 passed with outbound access denied ([ADR-021](../decisions/ADR-021-aws-emulator.md#stage-1-results-2026-10-30)).
- Stage 2, the first vertical scenario, on 2026-10-30. `AWS-S1` passes end to end: Sentinel diagnoses the poison message on SQS, rejects the innocent ECS revision, quarantines the message after approval and verifies from the consumer's telemetry that the queue recovers, where the baseline would roll the revision back. Ingestion reads the AWS environment with one set of read credentials (`Ingestion:Providers:Aws`): ECS deployments by image tag, the topology each ECS task definition declares, the services it calls through service discovery and the queues it names, and SQS queue attributes as metric series, each series naming its provider. Executor quarantines through a short redrive window on the queue. Gate check 8 passed within its budget ([ADR-021](../decisions/ADR-021-aws-emulator.md#stage-2-budget-2026-10-30)), and the run found an emulator incompatibility and a telemetry fact, both recorded there. The evaluation dataset is now `scenarios-v2`, with `S6` to `S8` and `AWS-S1`, which the earlier dataset left out.
- Stage 3, in part, on 2026-10-31. The AWS adapters pass Ingestion's provider conformance suite through a stubbed SDK transport. Scenario packages outside the local environment declare their fidelity, run records carry the environment, emulator and image digest they ran against, and the report labels each emulated scenario and never treats it as AWS's behaviour. The web app's journeys approve a quarantine plan.

Open:

- Stage 3: a web journey against the real system rather than the mock API, and replay of a captured AWS run. The CloudWatch adapters stay out: the emulator passes write-and-read-back, but no workload publishes to CloudWatch, so the hybrid OpenTelemetry path stays and the fidelity statement says so.
- Stage 4. `AWS-S7`, route drift, passed its emulator check on 2026-10-31 and can be admitted; it needs an EventBridge client in the reference system, Ingestion and Executor. `AWS-S4`, permission drift, is not admitted on the pinned image, which stores IAM policies but enforces none ([ADR-021](../decisions/ADR-021-aws-emulator.md#stage-4-admission-checks-2026-10-31)). `AWS-S12`, colliding resources in two accounts, needs Ingestion to read two accounts as two tenants.
- Stage 5, real-cloud validation, is deferred by the owner.

## Phase E02 – Frontend design-system depth

Status: **Complete** on 2026-10-30, apart from the second platform's screenshot baselines, which the owner deferred until CI exists. The second enhancement phase ([plan](README.md#enhancement-phases), [phase document](phase-e02-frontend-design-system-depth.md)). Its design source is the [frontend design-system plan](../architecture/frontend-design-system.md) and the [gap assessment of 2026-10-30](../architecture/frontend-design-system.md#gap-assessment-2026-10-30); the Phase 00 foundation it builds on is recorded under Phase 00 above.

Delivered:

- Tier 1. `Label`, `Input`, `Select` and `Checkbox` primitives and the `Field` and `ActionResult` patterns; the plan decision, narrative, replay, timeline and control-plane editors compose them, and no file under `src/features` or `src/app` writes a label or a result message by hand. Typography roles, an explicit spacing scale, elevation and stacking tokens, tabular figures on tables, confidence and activity, and the lint rule extended to Tailwind's own type sizes, z-index values and shadows in feature code, with fixture tests that run with the lint. The motion tokens drive the dialog and tooltip, the skeleton pulses only when motion is allowed, `Badge` lost its destructive variant, and the plan lists the recipe tokens and marks the chart and sidebar ones reserved ([tokens as implemented](../architecture/frontend-design-system.md#tokens-as-implemented)).
- Tier 2. Theme selection in the shell, dark by default, kept per browser and applied before the first paint, with the incident journey and a theme switch tested in light. A tablet project at 768 px runs the incident page, the light-theme journey and the control-plane navigation again, with screenshots and axe checks of the incident page and the control plane at both widths; below 768 px the navigation folds into a menu, and tables scroll.
- Tier 3. Interaction stories with `play` functions for the approval, rejection and revocation dialogs and an editor, keyboard only where it matters; the visual suite fails a story whose `play` function fails. `cn` comes from the local utility the shadcn config names, the display locale lives in one place, and the evidence graph is recorded as the visualization exception.
- The intermittent `publishes a new policy version` failure was a test filling a form before hydration; the suite now waits for the document to be hydrated.

Evidence: lint with the rule's fixtures, typecheck, 162 Storybook accessibility, screenshot and interaction tests, and 55 app tests at both widths, on 2026-10-30.

## Decisions this ledger surfaces

- Service-to-service authentication between the five services: [ADR-022](../decisions/ADR-022-service-to-service-authentication.md) is Proposed and awaits the owner's decision.
- Approver identity: [ADR-023](../decisions/ADR-023-approver-identity.md) is Proposed and awaits the owner's decision, although approvals are signed from Phase 01A and JWT identity exists.
- [ADR-020](../decisions/ADR-020-application-composition-and-cqrs.md) is still Proposed, although its refactoring sequence has been carried out.
- `packages/Sentinel.Testing` is a shared test-support package that [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md) does not list.
