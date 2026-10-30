# Phase status ledger

This ledger records what each [implementation phase](README.md) has delivered and which of its exit criteria remain open, with a link to the evidence for every claim. The phase documents define the work; this file is the only place that says how far it has got. A phase is **Complete** only when every exit criterion has evidence here. Update this file in the same change that moves a phase, and date the entry.

- Last reviewed: 2026-10-30, after every test project, the web tests, the evaluation harness and all six end-to-end scenarios passed; see [Recorded runs](#recorded-runs).
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
| [01 – Reference system](#phase-01--reference-system) | Complete | Reference services, deploy controller, scenario runner, scenarios `S0`–`S3`, baseline, tests | None open; no recorded pass |
| [01A – Walking skeleton](#phase-01a--walking-skeleton) | In progress | Golden path across all services, end-to-end suite, `S1`, `S2` and `S3` records, a live `S1` run on Muse Spark | Single-service restart test, CI check |
| [02 – Telemetry ingestion](#phase-02--telemetry-ingestion) | In progress | Six adapters with conformance tests, alerts, query contract with trust metadata, bulkhead, dead letters with tested replay, paging | Operational dashboard |
| [03 – Incident runtime](#phase-03--incident-runtime) | Complete | Event-sourced incidents, grains, Temporal workflow with every ADR-005 test, correlation, projections, PostgreSQL store tests | None |
| [04 – AI investigation](#phase-04--ai-investigation) | Complete | Six specialists, typed tools, Meta adapter, profiles and spend caps, findings validation, blackboard, a live run with findings | None |
| [05 – Verification and consensus](#phase-05--verification-and-consensus) | In progress | Deterministic adversarial verifier (`falsifiers-v2`), `consensus-v4` with dissent and abstention, replayable decisions | A correct diagnosis on `S3`; calibration |
| [06 – Safe remediation](#phase-06--safe-remediation) | Complete | Policy as code, autonomy modes, single-use capabilities, signed decisions, operator attestations, revocation, compensation, baseline-aware recovery | None; the managed signing facility is remaining scope |
| [07 – Memory, replay and evaluation](#phase-07--memory-replay-and-evaluation) | Complete | Incident memory, replay levels 1–3, postmortems, similar incidents, retention, scenario packages, evaluation harness, baseline report | None |
| [08 – Product experience](#phase-08--product-experience) | In progress | Live updates, real identity, tenant scoping, control-plane API and pages, manual overrides, notifications, analytics, responder pages | Real-system journey tests, identity provider |
| [09 – Production hardening](#phase-09--production-hardening) | In progress | Resilience and health, first runbooks, threat model, failure scenarios `S4` and `S5`, Sentinel SLOs | Deployment definition, service-to-service authentication, SLO alerting |
| [10 – Scale and evolution](#phase-10--scale-and-evolution) | Not started | Nothing; requires measured need | Not applicable yet |

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

## Phase 00 – Foundations

Status: **In progress**.

Delivered:

- Five services, each with Api, Application, Domain and Infrastructure projects, and the web app: [services](../../services/), [apps/Sentinel.Web](../../apps/Sentinel.Web/), [Sentinel.slnx](../../Sentinel.slnx); the SDK and packages are pinned in [global.json](../../global.json) and [Directory.Packages.props](../../Directory.Packages.props).
- Building blocks split into packages with provenance and a changelog: [CleanArchitecture.BuildingBlocks](../../packages/CleanArchitecture.BuildingBlocks/README.md), [CHANGELOG](../../packages/CleanArchitecture.BuildingBlocks/CHANGELOG.md), [ADR-013](../decisions/ADR-013-building-block-reuse.md). The messaging acceptance test (one crossing, a redelivered duplicate suppressed) is [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Typed identifiers and schema-versioned contracts with snapshots of the whole lifecycle: [identifiers](../../packages/Sentinel.Contracts/src/Sentinel.Contracts.Primitives/Identifiers/), [ContractSnapshotTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractSnapshotTests.cs), [ContractRulesTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractRulesTests.cs).
- Provider and model interfaces inside their owning services, alerts included ([IAlertProvider](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Abstractions/Providers/IAlertProvider.cs)), with a deterministic test double: [Ingestion providers](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Abstractions/Providers/), [IModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Abstractions/Providers/IModelProvider.cs), [DeterministicModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/DeterministicModelProvider.cs), [ADR-014](../decisions/ADR-014-provider-abstraction.md), [ADR-015](../decisions/ADR-015-model-abstraction.md).
- Architecture tests in every service enforcing layer direction, service isolation and ADR-020 rules 6 to 13, for example [Runtime's](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Architecture.Tests/LayerDependencyTests.cs). Isolation is checked on project references as well as on types, so a reference to another service fails even before any type uses it: [ProjectReferenceRuleTests](../../packages/Sentinel.Testing/tests/Sentinel.Testing.Tests/ProjectReferenceRuleTests.cs).
- Local bootstrap, health, OpenTelemetry and generated secret parameters: [AppHost](../../deploy/local/Sentinel.AppHost/AppHost.cs), [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), [HealthEndpointTests](../../packages/Sentinel.ServiceDefaults/tests/Sentinel.ServiceDefaults.Tests/HealthEndpointTests.cs).
- ADR-001 to ADR-020, with ADR-020 still Proposed. Templates: [ADR](../decisions/TEMPLATE.md), [schema change](../business-contracts/schema-change-template.md).
- Design system with tokens, themes, stories, accessibility and screenshot checks: [design-system](../../apps/Sentinel.Web/src/design-system/), [storybook.spec.ts](../../apps/Sentinel.Web/tests/visual/storybook.spec.ts) with baselines in [tests/__screenshots__](../../apps/Sentinel.Web/tests/__screenshots__/), [check-design-system.mjs](../../apps/Sentinel.Web/scripts/check-design-system.mjs).
- Repeatable tasks: [scripts/check.ps1](../../scripts/check.ps1), [PROJECT.json](../../PROJECT.json).
- The tenant, environment, service and incident on every consumer span and in the log scope of every message handler, and on incident requests at the Gateway, under the names in [SentinelTags](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/SentinelTags.cs): [MessageContextTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.Tests/MessageContextTests.cs), [TenantScopeTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/TenantScopeTests.cs).

Open:

- A CI pipeline. None exists, so the design-system and package rules that require checks in CI are open too.
- Missing-secret negative tests beyond model credentials and the reference control token.

## Phase 01 – Reference system

Status: **Complete** by repository evidence; no pass is recorded.

Delivered:

- Checkout, orders, the reporting worker and the Redis exporter, with healthy and faulty revisions and each fault in its own file: [Reference.Checkout](../../reference-system/services/Reference.Checkout/), [Reference.Orders](../../reference-system/services/Reference.Orders/), [Reference.ReportingWorker](../../reference-system/services/Reference.ReportingWorker/), [RedisBehaviourTests](../../reference-system/tests/Reference.Tests/RedisBehaviourTests.cs), [RevisionBehaviourTests](../../reference-system/tests/Reference.Tests/RevisionBehaviourTests.cs), [ADR-011](../decisions/ADR-011-reference-system-location.md).
- The deploy controller with its ledger, and the scenario runner with traffic, reset and timeline execution: [DeploymentLedger](../../reference-system/deploy-controller/Reference.DeployController.Domain/Revisions/DeploymentLedger.cs), [ScenarioRunner](../../reference-system/scenarios/Reference.Scenarios.Application/Runs/ScenarioRunner.cs), [DeploymentLedgerTests](../../reference-system/tests/Reference.Tests/DeploymentLedgerTests.cs), [DeployControllerEndpointTests](../../reference-system/tests/Reference.Tests/DeployControllerEndpointTests.cs), [ScenarioRunnerEndpointTests](../../reference-system/tests/Reference.Tests/ScenarioRunnerEndpointTests.cs).
- Failure controls exposed only when enabled, and guarded by a token: [FailureControl](../../reference-system/services/Reference.Common/Control/FailureControl.cs).
- Manifests `S0`–`S3` with ground truth, discriminating signals and recovery, and the baseline definition: [manifests](../../reference-system/scenarios/manifests/), [ScenarioManifestTests](../../reference-system/tests/Reference.Tests/ScenarioManifestTests.cs), [deploy-blame.md](../../reference-system/scenarios/baseline/deploy-blame.md).
- Symptom, signal and recovery checks for `S1`–`S3` across the real stack: [ReferenceScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/ReferenceScenarioTests.cs).

Caveats, not exit criteria:

- "Runs repeatedly" rests on reset idempotency rather than on a repeated run.
- `ReferenceScenarioTests` does not run `S0`.
- No test checks trace continuity across checkout, orders and Redis.

## Phase 01A – Walking skeleton

Status: **In progress**.

Delivered:

- The golden path end to end. `S1` runs from the break to approval in the browser, a single rollback and verified recovery; `S2` and `S3` run through decision and plan; `S0` opens no incident: [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs).
- Approval signed in the Gateway and verified in Executor, with negative tests: [ApprovePlanCommandHandler](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/BusinessTasks/Commands/Approvals/ApprovePlanCommandHandler.cs), [LocalApprovalSigner](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Infrastructure/Security/LocalApprovalSigner.cs), [AttestationVerifier](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/Services/AttestationVerifier.cs), [AttestationTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/AttestationTests.cs), [ApprovalEndpointTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/ApprovalEndpointTests.cs).
- The deploy-blame baseline running in shadow: [DeployBlameInvestigator](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/DeployBlameInvestigator.cs).
- Idempotent handling of duplicate delivery: [RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs), [InboundMessageProcessorTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.Tests/InboundMessageProcessorTests.cs).
- The comparison report, with `S1`, `S2` and `S3` records: [comparison.md](../../artifacts/reports/walking-skeleton/comparison.md). `S1` ran from the break through approval in the browser to a verified rollback and recovery on 2026-10-30: [S1.outcome.json](../../artifacts/reports/walking-skeleton/S1.outcome.json).
- A live run on `muse-spark-1.3`, with every role on the `meta` profile, documented with its agent executions: `S1` diagnosed the v2.4.1 deployment with confidence 0.87, proposed the rollback, and recovered after approval, for an estimated $0.24 ([S1.meta.json](../../artifacts/reports/evaluation/runs/S1.meta.json)). Two earlier live runs that day exposed the defects fixed by prompt `investigator-v4` (71db520) and `consensus-v3` (fcccfce); an earlier live `S2` run on 2026-10-29 produced no findings, and its record was overwritten before the harness kept live runs apart (9c2dd19).

Open:

- A test that restarts a single service between steps without repeating the rollback.
- The deterministic CI run, and the end-to-end suite as a required CI check.

## Phase 02 – Telemetry ingestion

Status: **In progress**.

Delivered:

- Prometheus, Loki, Jaeger, deploy-controller and runbook adapters with conformance tests: [Providers](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/), [ProviderConformanceTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderConformanceTests.cs), [SignalAdapterTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/SignalAdapterTests.cs).
- Checkpoints, backoff, late data and deterministic IDs: [IngestionCycle](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Services/IngestionCycle.cs), [SourceState](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Domain/Sources/SourceState.cs), [IngestionCycleTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/IngestionCycleTests.cs), [SourceStateTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/Domain/SourceStateTests.cs), [ADR-016](../decisions/ADR-016-event-time-and-late-data.md).
- Redaction before storage: [RedactionPolicy](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Domain/Observations/RedactionPolicy.cs), [RedactionTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/RedactionTests.cs).
- The query contract with trust metadata, and the query bulkhead: [QueryObservationsQueryHandler](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/Observations/QueryObservationsQueryHandler.cs), [QueryBulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/QueryBulkhead.cs), [IngestionEndpointTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/IngestionEndpointTests.cs).
- Prioritized pull loops, bounded provider clients and ingestion telemetry: [IngestionWorker](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Workers/IngestionWorker.cs), [IngestionTelemetry](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Diagnostics/IngestionTelemetry.cs), [ADR-018](../decisions/ADR-018-observation-publication-scope.md).
- Dead letters, with operator list and replay endpoints: [OperatorEndpoints](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/OperatorEndpoints.cs). Replay after the cause is fixed is tested: [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Paging through continuation tokens against PostgreSQL: [PostgresObservationStoreTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/PostgresObservationStoreTests.cs).
- A compatibility snapshot of the query answer, which pins the log, trace, deployment, topology and alert observations: [ingestion.query.observations.result.json](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/Snapshots/ingestion.query.observations.result.json).
- Alerts as a signal type: the reference Prometheus raises `CheckoutSloFastBurn` and `RedisClientsNearLimit`, Ingestion reads the pending and firing alerts on every poll as [AlertObservation](../../packages/Sentinel.Contracts/src/Sentinel.Contracts/Observations/AlertObservation.cs)s, with each state its own observation and annotations redacted, and answers alert queries. Alerts are stored and queried but not published, as [ADR-018](../decisions/ADR-018-observation-publication-scope.md) publishes only kinds with an event consumer: [PrometheusAlertsProvider](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/PrometheusAlertsProvider.cs), [ProviderConformanceTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderConformanceTests.cs), [slo.yml](../../reference-system/deploy/prometheus/rules/slo.yml).

Open:

- An operational dashboard beyond the Aspire dashboard and `/v1/sources`.

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

Open from the test plan, not exit criteria:

- Tests for tenant isolation in tools, and for the Meta adapter's rate limiting.

## Phase 05 – Verification and consensus

Status: **In progress**.

Delivered:

- A deterministic adversarial verifier (`falsifiers-v2`) in AgentWorker that challenges hypotheses and proposes alternatives. Version 2 falsifies a change that was replaced before the symptoms began, and measures a change's mechanism from just before it was served, so an earlier fault cannot pass as its effect: [AdversarialVerifier](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/AdversarialVerifier.cs), [VerifierTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/VerifierTests.cs).
- `consensus-v4` with independence families, contradiction weighting, dissent, abstention and an inputs hash. Version 2 stopped counting the leading change's own mechanism, such as Redis saturation in the changed service, as its rival. Version 3 also lets a standing change claim the saturation investigators blamed on its own service, so the saturation's evidence counts for the change even when the saturation scored higher alone, as it did in the first live run with findings. Version 4 lets a change claim the regression of its own service in the same way, which had left `S3` abstaining with its true cause in the lead. Each decision replays by the version it was made with: [Consensus](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Consensus.cs), [ConsensusSettings](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/ConsensusSettings.cs), [ConsensusTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/ConsensusTests.cs).
- Confidence and hypothesis comparison in the web app: [confidence-display](../../apps/Sentinel.Web/src/design-system/domain/confidence-display.tsx), [hypothesis-comparison](../../apps/Sentinel.Web/src/design-system/patterns/hypothesis-comparison.tsx).

Open:

- Beating the baseline on `S1`–`S3`. With the test double, Sentinel diagnoses `S1` and `S2` correctly and abstains on `S3`, with no false remediation, while the baseline remediates falsely on `S2` and `S3`; the live model also diagnosed `S1` correctly ([report](../../artifacts/reports/evaluation/report.md)). The criterion also asks for a correct diagnosis on `S3`, which is still open, and live runs of `S2` and `S3`.
- Calibration fixtures and reliability tracking. Calibration is reported as uncalibrated.

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
- Product analytics sent from the web app for every event in the catalog, honouring Global Privacy Control and Do Not Track: [track.ts](../../apps/Sentinel.Web/src/features/analytics/track.ts), [product-analytics.md](../business-contracts/product-analytics.md).
- The external API documented with its conventions, idempotency, paging, the live stream and the control plane: [synchronous contracts](../business-contracts/synchronous-contracts.md#the-gateways-external-http-api).

Open:

- Journey tests against the real system. The web tests run against a mock API, and only approval is driven through the UI end to end.
- A wired identity provider, and an ADR for approver identity.
- Performance tests for large timelines and evidence graphs.
- Structured editors for configuration areas. Each area is edited as its JSON document, which the owning service validates.

## Phase 09 – Production hardening

Status: **In progress**.

Delivered:

- Resilience and health in [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), the [query bulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/QueryBulkhead.cs), and the Temporal restart and backup tests.
- The first runbooks: emergency stop, models and spend, dead letters and projection rebuild ([runbooks](../runbooks/README.md)).
- A threat model with the trust boundaries, the open risks and the status of every review finding: [threat-model.md](../architecture/threat-model.md). Three findings are open: F-007 and F-013 wait on service-to-service authentication, and F-011 on a model-result cache.
- End-to-end failure scenarios: the golden incident with the logs provider down (`S4`) and with a rollback that fails (`S5`): [FailureScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/FailureScenarioTests.cs).
- Sentinel's own SLOs for freshness, detection, decision time, event delivery and availability, each measured by a metric the services export, with guardrails that have no error budget: [service-level objectives](../architecture/service-level-objectives.md). Runtime emits the incident timings and outcomes they need: [RuntimeTelemetry](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Diagnostics/RuntimeTelemetry.cs), [RuntimeTelemetryTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeTelemetryTests.cs).

Open:

- A deployment definition beyond `deploy/local/`, which waits on the choice of production environment.
- Service-to-service authentication, which has no ADR. Until then Runtime's operator endpoints stay unauthenticated ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)).
- Alerting on the SLOs. They are defined and measured, but nothing pages anyone yet; that arrives with the deployment definition.
- Failure injection beyond `S4` and `S5`, such as a service restarted between steps or a slow database.
- Runbooks for deployment, key rotation and restore, which arrive with the deployment definition.
- The local stack sometimes fails to start: in two of eleven end-to-end runs on 2026-10-29 and 30, the Temporal container was never created, so Runtime never started and every test failed at once. A rerun succeeded each time, and the cause is not yet known; the next occurrence should be read from Docker's events for the Temporal image.

## Phase 10 – Scale and evolution

Status: **Not started**. Requires measured need and an ADR per item.

## Decisions this ledger surfaces

- Service-to-service authentication between the five services has no ADR; the [overview](../architecture/overview.md) lists it as open.
- Approver identity has no ADR, although approvals are signed from Phase 01A and JWT identity exists.
- [ADR-020](../decisions/ADR-020-application-composition-and-cqrs.md) is still Proposed, although its refactoring sequence has been carried out.
- `packages/Sentinel.Testing` is a shared test-support package that [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md) does not list.
