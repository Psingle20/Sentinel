# Phase status ledger

This ledger records what each [implementation phase](README.md) has delivered and which of its exit criteria remain open, with a link to the evidence for every claim. The phase documents define the work; this file is the only place that says how far it has got. A phase is **Complete** only when every exit criterion has evidence here. Update this file in the same change that moves a phase, and date the entry.

- Last reviewed: 2026-10-29, by repository inspection after the [ADR-020](../decisions/ADR-020-application-composition-and-cqrs.md) restructure. The `build` and `lint` tasks in `scripts/check.ps1` passed on that date. The full `test` task, including the end-to-end suites, was running when this review was written; its result goes under [Recorded runs](#recorded-runs). Until it is recorded, where a row relies on a test, the test's presence is the evidence, not a recorded pass.
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
| [00 – Foundations](#phase-00--foundations) | In progress | Five layered services and the web app, building blocks, contracts with snapshots, design system, architecture tests, 20 ADRs | CI pipeline; tenant in logs and traces; alert provider interface |
| [01 – Reference system](#phase-01--reference-system) | Complete | Reference services, deploy controller, scenario runner, scenarios `S0`–`S3`, baseline, tests | None open; no recorded pass |
| [01A – Walking skeleton](#phase-01a--walking-skeleton) | In progress | Golden path across all services, end-to-end suite, `S2` and `S3` records, one live model run | `S1` outcome record, single-service restart test, CI check |
| [02 – Telemetry ingestion](#phase-02--telemetry-ingestion) | In progress | Five adapters with conformance tests, query contract with trust metadata, bulkhead, dead letters | Alert signal, operational dashboard, truncation and continuation tests |
| [03 – Incident runtime](#phase-03--incident-runtime) | In progress | Event-sourced incidents, grains, Temporal workflow with ADR-005 tests, correlation, projections | A recorded pass; the combined Runtime-and-Temporal failure test |
| [04 – AI investigation](#phase-04--ai-investigation) | In progress | Six specialists, typed tools, Meta adapter and routing, findings validation, blackboard | A live run that produces findings; model failure metrics evidenced |
| [05 – Verification and consensus](#phase-05--verification-and-consensus) | In progress | Deterministic adversarial verifier, `consensus-v1` with dissent and abstention, replayable decisions | Beating the baseline on `S1`–`S3`; calibration |
| [06 – Safe remediation](#phase-06--safe-remediation) | In progress | Policy as code, autonomy modes, single-use capabilities, key rotation, revocation, compensation, baseline-aware recovery | Managed signing facility; three test gaps; a recorded end-to-end pass |
| [07 – Memory, replay and evaluation](#phase-07--memory-replay-and-evaluation) | In progress | Incident memory, replay levels 1–3, postmortems, similar incidents, retention, scenario packages, evaluation harness | Baseline evaluation report; golden incident replayed offline; level 4 |
| [08 – Product experience](#phase-08--product-experience) | In progress | Live updates, real identity, tenant scoping, control-plane API, notifications, analytics, responder pages | Configuration UI, real-system journey tests, API contract docs |
| [09 – Production hardening](#phase-09--production-hardening) | Not started | Resilience and health in the building blocks and service defaults | Everything, including runbooks and a deployment definition |
| [10 – Scale and evolution](#phase-10--scale-and-evolution) | Not started | Nothing; requires measured need | Not applicable yet |

## Recorded runs

| Date | Task | Result | Notes |
| --- | --- | --- | --- |
| 2026-10-29 | `build` | Passed | After the ADR-020 restructure, with the one-type-per-file rules at error. |
| 2026-10-29 | `lint` | Passed | |
| 2026-10-29 | `test` | Running | Result to be recorded here. |

## Phase 00 – Foundations

Status: **In progress**.

Delivered:

- Five services, each with Api, Application, Domain and Infrastructure projects, and the web app: [services](../../services/), [apps/Sentinel.Web](../../apps/Sentinel.Web/), [Sentinel.slnx](../../Sentinel.slnx); the SDK and packages are pinned in [global.json](../../global.json) and [Directory.Packages.props](../../Directory.Packages.props).
- Building blocks split into packages with provenance and a changelog: [CleanArchitecture.BuildingBlocks](../../packages/CleanArchitecture.BuildingBlocks/README.md), [CHANGELOG](../../packages/CleanArchitecture.BuildingBlocks/CHANGELOG.md), [ADR-013](../decisions/ADR-013-building-block-reuse.md). The messaging acceptance test (one crossing, a redelivered duplicate suppressed) is [MessageDeliveryTests](../../packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests/MessageDeliveryTests.cs).
- Typed identifiers and schema-versioned contracts with snapshots of the whole lifecycle: [identifiers](../../packages/Sentinel.Contracts/src/Sentinel.Contracts.Primitives/Identifiers/), [ContractSnapshotTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractSnapshotTests.cs), [ContractRulesTests](../../packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/ContractRulesTests.cs).
- Provider and model interfaces inside their owning services, with a deterministic test double: [Ingestion providers](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/Abstractions/Providers/), [IModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Abstractions/Providers/IModelProvider.cs), [DeterministicModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/DeterministicModelProvider.cs), [ADR-014](../decisions/ADR-014-provider-abstraction.md), [ADR-015](../decisions/ADR-015-model-abstraction.md).
- Architecture tests in every service enforcing layer direction, service isolation and ADR-020 rules 6 to 13, for example [Runtime's](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Architecture.Tests/LayerDependencyTests.cs).
- Local bootstrap, health, OpenTelemetry and generated secret parameters: [AppHost](../../deploy/local/Sentinel.AppHost/AppHost.cs), [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), [HealthEndpointTests](../../packages/Sentinel.ServiceDefaults/tests/Sentinel.ServiceDefaults.Tests/HealthEndpointTests.cs).
- ADR-001 to ADR-020, with ADR-020 still Proposed. Templates: [ADR](../decisions/TEMPLATE.md), [schema change](../business-contracts/schema-change-template.md).
- Design system with tokens, themes, stories, accessibility and screenshot checks: [design-system](../../apps/Sentinel.Web/src/design-system/), [storybook.spec.ts](../../apps/Sentinel.Web/tests/visual/storybook.spec.ts) with baselines in [tests/__screenshots__](../../apps/Sentinel.Web/tests/__screenshots__/), [check-design-system.mjs](../../apps/Sentinel.Web/scripts/check-design-system.mjs).
- Repeatable tasks: [scripts/check.ps1](../../scripts/check.ps1), [PROJECT.json](../../PROJECT.json).

Open:

- A CI pipeline. None exists, so the design-system and package rules that require checks in CI are open too.
- The tenant in logs and traces. Service defaults add only trace and correlation IDs; the tenant travels in [message headers](../../packages/Sentinel.Contracts/src/Sentinel.Contracts/Messaging/SentinelHeaders.cs).
- Missing-secret negative tests beyond model credentials and the reference control token.
- The alert provider interface the phase lists.

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
- The comparison report, with `S2` and `S3` records: [comparison.md](../../artifacts/reports/walking-skeleton/comparison.md). One live Muse Spark run exists, for `S2`: [runs/S2.json](../../artifacts/reports/evaluation/runs/S2.json).

Open:

- The `S1` outcome record and its comparison row.
- A live model run that produces findings. In the `S2` run, one specialist exhausted its token budget, one returned invalid output after a repair attempt, and four timed out.
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
- Dead letters, with operator list and replay endpoints: [OperatorEndpoints](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/OperatorEndpoints.cs).

Open:

- The alert signal type: no observation, provider interface or adapter exists for it.
- An operational dashboard beyond the Aspire dashboard and `/v1/sources`.
- Tests for truncation and continuation tokens, and for dead-letter replay.
- Compatibility snapshots for log, trace, deployment and topology observations.

## Phase 03 – Incident runtime

Status: **In progress**. Every exit criterion has a test. The phase becomes Complete when a recorded `test` pass covers those tests and the ADR-005 test of Runtime and Temporal failing together exists.

Delivered:

- The incident aggregate with its child lifecycles and state tables: [Incident](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Incident.cs), [IncidentStateMachine](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/IncidentStateMachine.cs), [IncidentStateMachineTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentStateMachineTests.cs).
- Correlation with absorb, merge, open, reopen and suppress decisions: [CorrelationEngine](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Correlation/CorrelationEngine.cs), [CorrelationEngineTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/CorrelationEngineTests.cs).
- Grains as single writers, with restart and silo-loss tests: [Grains](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Grains/), [IncidentGrainClusterTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentGrainClusterTests.cs).
- The event store with optimistic concurrency, projections and rebuild: [PostgresIncidentStore](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Persistence/PostgresIncidentStore.cs), [IncidentProjector](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Projections/IncidentProjector.cs).
- The Temporal workflow, with the ADR-005 acceptance tests run on a real server: a worker killed mid-run, a versioned upgrade, a server restart, backup and restore, and duplicate, rejected and expired approvals. See [IncidentWorkflow](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs) and [IncidentWorkflowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentWorkflowTests.cs).
- Behaviour tests in [RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs): one incident for the golden burst, none for `S0`, duplicate and concurrent commands, and deleted then rebuilt projections.

Open, not exit criteria unless stated:

- The ADR-005 test of Runtime and Temporal failing together. This one is an exit criterion.
- Tests against the real PostgreSQL store. Every Runtime test uses the in-memory store.
- A correlation split rule.
- Review finding F-002: an `OutcomeUnknown` execution result escalates and ends the workflow instead of waiting for reconciliation ([IncidentWorkflow](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs)).

## Phase 04 – AI investigation

Status: **In progress**.

Delivered:

- Six specialists and a generalist, each limited to its role's tools, inside an execution with token, tool and repair budgets: [Specialists](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/Specialists.cs), [Tools](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Tools/), [AgentExecution](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Domain/Executions/AgentExecution.cs), [SpecialistTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/SpecialistTests.cs), [AgentExecutionTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/Domain/AgentExecutionTests.cs).
- The Meta adapter, the test double, per-role routing and a versioned prompt: [MetaModelProvider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/MetaModelProvider.cs), [ModelRouter](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/ModelRouter.cs), [InvestigatorPrompt](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/InvestigatorPrompt.cs).
- Findings validation, tested in [FindingsValidatorTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/FindingsValidatorTests.cs) and [InvestigationTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/InvestigationTests.cs):
  - claims cite observation IDs and tool calls;
  - competing hypotheses are required;
  - invalid output abstains;
  - proposed actions are refused.
- The hypothesis blackboard and causal graph: [HypothesisBlackboard](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/HypothesisBlackboard.cs), [BlackboardTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/BlackboardTests.cs).
- A prompt-injection runbook fixture: [Telemetry.cs](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/Telemetry.cs), [reporting-worker.md](../../reference-system/knowledge/reporting-worker.md).

Open:

- A live model run that produces findings (see Phase 01A).
- Evidence for the model failure and schema-failure counters. The existing test covers only tokens, latency and cost.
- A prompt-injection fixture planted in logs.
- Tests for tenant isolation in tools, and for the Meta adapter's rate limiting.

## Phase 05 – Verification and consensus

Status: **In progress**.

Delivered:

- A deterministic adversarial verifier (`falsifiers-v1`) in AgentWorker that challenges hypotheses and proposes alternatives: [AdversarialVerifier](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/AdversarialVerifier.cs), [VerifierTests](../../services/Sentinel.AgentWorker/tests/Sentinel.AgentWorker.Tests/VerifierTests.cs).
- `consensus-v1` with independence families, contradiction weighting, dissent, abstention and an inputs hash; its decisions replay identically: [Consensus](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Consensus.cs), [ConsensusTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/ConsensusTests.cs).
- Confidence and hypothesis comparison in the web app: [confidence-display](../../apps/Sentinel.Web/src/design-system/domain/confidence-display.tsx), [hypothesis-comparison](../../apps/Sentinel.Web/src/design-system/patterns/hypothesis-comparison.tsx).

Open:

- Beating the baseline on `S1`–`S3`:
  - `S1` has no record.
  - `S3`, run with the test double, abstained and is scored incorrect. It made no false remediation, whereas the baseline did.
  - `S2`, run with the live model, abstained because no specialist produced findings.
- Calibration fixtures and reliability tracking. Calibration is reported as uncalibrated.

## Phase 06 – Safe remediation

Status: **In progress**.

Delivered:

- Policy as code with a rule trace, evaluating emergency stop and disabled actions first, then rules by environment, target, risk, confidence, blast radius, severity and freezes: [PolicyEngine](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Policy/PolicyEngine.cs), [remediation-policy.json](../../services/Sentinel.Executor/src/Sentinel.Executor.Api/policies/remediation-policy.json), [PolicyTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/PolicyTests.cs).
- Four autonomy modes, applied as a per-environment ceiling: [AutonomyMode](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Policy/AutonomyMode.cs).
- Single-use capabilities with scope, limits and expiry: [Capability](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Capabilities/Capability.cs), [ExecutionTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/ExecutionTests.cs).
- Signing key rotation, and the approval aggregate with revocation and expiry: [LocalApprovalSignerTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/LocalApprovalSignerTests.cs), [Approval](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Domain/Approvals/Approval.cs), [ApprovalTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/Domain/ApprovalTests.cs), [RemediationLifecycleTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RemediationLifecycleTests.cs).
- The execution state machine with fencing, start confirmation, `OutcomeUnknown` reconciliation and compensation in reverse order: [ExecutionRequest](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Executions/ExecutionRequest.cs), [ReconciliationWorker](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Execution/ReconciliationWorker.cs).
- Rollback and batch adapters: [DeployControllerRollbackAdapter](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Remediation/DeployControllerRollbackAdapter.cs), [ReportingBatchAdapter](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Remediation/ReportingBatchAdapter.cs).
- Recovery judged against the pre-action baseline: [RecoveryAssessor](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/Recovery/RecoveryAssessor.cs), [RecoveryCriteriaTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RecoveryCriteriaTests.cs).
- End-to-end tests of the policy result and a single rollback on the golden path, and of a failed rollback escalated without retry: [GoldenPathTests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs), [FailureScenarioTests](../../deploy/local/Sentinel.EndToEnd.Tests/FailureScenarioTests.cs).

Open:

- A managed, non-exportable signing facility. Only the local signer exists.
- Tests for:
  - a plan superseded after approval;
  - truncated or partial telemetry reaching the recovery assessor;
  - crashes in the `Received` and `StartConfirmed` states.
- A recorded end-to-end pass with policy as code and recovery verification on the path.

## Phase 07 – Memory, replay and evaluation

Status: **In progress**.

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

Open:

- The baseline evaluation report (`artifacts/reports/evaluation/report.md` and `report.json`), and schema-2 runs for `S0`, `S1`, `S3`, `S4` and `S5`. Only `S2` exists.
- The golden incident replayed without live providers or actions. There is no `S1` run record and no replay in the end-to-end suite.
- Replay level 4, system simulation. Only the enum value exists.
- Tests for the retention SQL in the PostgreSQL memory store, and for stable version manifests.
- A regression-kind scenario package.

## Phase 08 – Product experience

Status: **In progress**.

Delivered:

- Live updates over server-sent events, resumable, with reconnection and a polling fallback in the web app: [LiveController](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Controllers/LiveController.cs), [live-updates.tsx](../../apps/Sentinel.Web/src/features/incidents/live-updates.tsx).
- JWT bearer identity with three roles, a development identity available only in Development, and tenant scoping: [IdentityExtensions](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Identity/IdentityExtensions.cs), [IdentityTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/IdentityTests.cs), [IncidentTenantFilter](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Filters/IncidentTenantFilter.cs).
- Idempotency keys on mutations, and paginated timeline and evidence: [IdempotencyMiddleware](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Idempotency/IdempotencyMiddleware.cs), [Pages](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Incidents/Pages.cs).
- The control-plane API with audit, and each service's configuration document: [AdminController](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Controllers/AdminController.cs), [ControlPlaneAudit](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/ControlPlane/ControlPlaneAudit.cs), [GatewayEndpointTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/GatewayEndpointTests.cs).
- Notification routing and privacy-safe product analytics: [NotificationRouter](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Live/NotificationRouter.cs), [AnalyticsCatalog](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Application/Analytics/AnalyticsCatalog.cs).
- Responder, postmortem and admin pages, tested in [incidents.spec.ts](../../apps/Sentinel.Web/tests/e2e/incidents.spec.ts): [incident-detail.tsx](../../apps/Sentinel.Web/src/features/incidents/incident-detail.tsx), [postmortem page](../../apps/Sentinel.Web/src/app/incidents/[incidentId]/postmortem/page.tsx), [admin page](../../apps/Sentinel.Web/src/app/admin/page.tsx).

Open:

- The configuration-area and manual-override UI. The server actions exist, but no page uses them yet. Control-plane sub-pages are in progress but not yet in the main tree.
- Journey tests against the real system. The web tests run against a mock API, and only approval is driven through the UI end to end.
- A wired identity provider, and an ADR for approver identity.
- Performance tests for large timelines and evidence graphs.
- API contract documentation for live events, the admin API, analytics and pages.

## Phase 09 – Production hardening

Status: **Not started**.

Present: resilience and health in [service defaults](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/ServiceDefaultsExtensions.cs), the [query bulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/BusinessTasks/Queries/QueryBulkhead.cs), and the Temporal restart and backup tests.

Open: everything in the phase, including:

- runbooks in `docs/runbooks/`, which is empty;
- a deployment definition beyond `deploy/local/`;
- a threat model;
- Sentinel's own SLOs;
- failure-injection suites.

Runtime's operator endpoints stay unauthenticated until this phase ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)).

## Phase 10 – Scale and evolution

Status: **Not started**. Requires measured need and an ADR per item.

## Decisions this ledger surfaces

- Service-to-service authentication between the five services has no ADR; the [overview](../architecture/overview.md) lists it as open.
- Approver identity has no ADR, although approvals are signed from Phase 01A and JWT identity exists.
- [ADR-020](../decisions/ADR-020-application-composition-and-cqrs.md) is still Proposed, although its refactoring sequence has been carried out.
- `packages/Sentinel.Testing` is a shared test-support package that [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md) does not list.
