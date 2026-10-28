# Phase status ledger

This ledger records what each [implementation phase](README.md) has delivered and which of its exit criteria remain open, with a link to the evidence for every claim. The phase documents define the work; this file is the only place that says how far it has got. A phase is **Complete** only when every exit criterion has evidence here. Update this file in the same change that moves a phase, and date the entry.

- Last reviewed: 2026-10-28, by repository inspection. The `build` task in `scripts/check.ps1` passed on that date. Unit, integration and end-to-end suites were not executed for this review; where a row relies on a test, the test's presence is the evidence, not a recorded pass.
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
| [00 – Foundations](#phase-00--foundations) | In progress | Hosts, packages, design system, architecture tests, 11 ADRs | CI pipeline |
| [01 – Reference system](#phase-01--reference-system) | Complete | Reference services, scenarios `S0`–`S3`, baseline, scenario tests | None open; test pass not recorded |
| [01A – Walking skeleton](#phase-01a--walking-skeleton) | In progress | Golden path across all services, end-to-end suite, comparison report | `S1` outcome record, live model run, CI check |
| [02 – Telemetry ingestion](#phase-02--telemetry-ingestion) | In progress | Logs, metrics, traces, deployments, topology, knowledge; query contract with freshness metadata; outbox, inbox and DLQ | Alert signal type, dashboard beyond Aspire, exit-criteria evidence |
| [03 – Incident runtime](#phase-03--incident-runtime) | In progress | `IncidentGrain`, `CorrelationGrain`, `ServiceGrain`, Temporal workflow, event-sourced store, projection rebuild | ADR-005 acceptance tests, `S0` produces no incident |
| [04 – AI investigation](#phase-04--ai-investigation) | In progress | Specialists, six typed tools, Meta adapter and routing, findings validation, blackboard, causal links | Live model run, prompt-injection fixtures, model telemetry evidence |
| [05 – Verification and consensus](#phase-05--verification-and-consensus) | In progress | Challenges with falsifiers, `consensus-v1` with independence, dissent and abstention | Adversarial agent in AgentWorker, calibration, beating the baseline on `S1`–`S3` |
| [06 – Safe remediation](#phase-06--safe-remediation) | Skeleton | Static policy, attestation verification, execution state machine, one rollback adapter, telemetry-gated recovery | Policy-as-code, modes, capabilities, key rotation, compensation |
| [07 – Memory, replay and evaluation](#phase-07--memory-replay-and-evaluation) | Skeleton | Exported run records and comparison report | Everything else |
| [08 – Product experience](#phase-08--product-experience) | Skeleton | Incident list and detail with polling, approve and reject, development identities | Live updates, real identity, control plane, notifications |
| [09 – Production hardening](#phase-09--production-hardening) | Not started | Resilience and health in building blocks only | Everything, including runbooks and a deployment definition |
| [10 – Scale and evolution](#phase-10--scale-and-evolution) | Not started | Nothing; requires measured need | Not applicable yet |

## Phase 00 – Foundations

Status: **In progress**.

Delivered:

- Five service hosts with Domain, Application, Infrastructure and Host projects under `services/`, and `apps/Sentinel.Web`.
- `global.json` pins .NET SDK 10.0.302 and Aspire 13.6.4; `Directory.Packages.props` pins packages centrally.
- Building blocks split into Core, Application, Infrastructure, PostgreSql, Redis and Hosting, through Stage E ([changelog](../../packages/CleanArchitecture.BuildingBlocks/CHANGELOG.md), [evolution plan](building-blocks-evolution.md), [ADR-013](../decisions/ADR-013-building-block-reuse.md)).
- Architecture tests per service rejecting forbidden dependency directions, for example [Runtime](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Architecture.Tests/LayerDependencyTests.cs).
- Frontend design system with tokens, themes, primitives, domain components and patterns under `apps/Sentinel.Web/src/design-system/`; Storybook configuration and Playwright visual baselines under `apps/Sentinel.Web/tests/visual/`.
- Provider and model abstractions with test doubles ([ADR-014](../decisions/ADR-014-provider-abstraction.md), [ADR-015](../decisions/ADR-015-model-abstraction.md), [deterministic model provider](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/DeterministicModelProvider.cs)).
- Persistence, transport, orchestration and event-semantics decisions ([ADR-008](../decisions/ADR-008-event-transport.md), [ADR-009](../decisions/ADR-009-persistence-baseline.md), [ADR-010](../decisions/ADR-010-local-orchestration.md), [ADR-012](../decisions/ADR-012-event-semantics.md)).
- Repeatable `setup`, `build`, `test` and `lint` tasks in `scripts/check.ps1`, mirrored in `PROJECT.json`.

Open:

- **CI pipeline** (deliverable 6). No pipeline definition exists in the repository. The Phase 01A rule that the end-to-end suite is a required check for every later phase therefore has nothing to attach to.

## Phase 01 – Reference system

Status: **Complete** by repository evidence.

Delivered:

- Checkout, order and reporting-worker services, Redis, redis-exporter, Prometheus, Loki, Jaeger and the deploy controller ([reference-system/README.md](../../reference-system/README.md), [ADR-011](../decisions/ADR-011-reference-system-location.md)).
- Scenario manifests `S0`–`S3` under `reference-system/scenarios/manifests/` and the [deploy-blame baseline](../../reference-system/scenarios/baseline/deploy-blame.md).
- Scenario runner with run, reset and stop-batch operations behind a generated control token.
- [Reference scenario tests](../../deploy/local/Sentinel.EndToEnd.Tests/ReferenceScenarioTests.cs) asserting that `S0` does not burn the SLO and that `S1`–`S3` show their symptoms, discriminating signals and recovery.

Open:

- None. A recorded pass of the scenario tests would close the remaining doubt; this review did not run them.

## Phase 01A – Walking skeleton

Status: **In progress**.

Delivered:

- [Golden path tests](../../deploy/local/Sentinel.EndToEnd.Tests/GoldenPathTests.cs): `S1` from break to approved rollback and verified recovery with Playwright approval, and `S2`, `S3` through decision and plan.
- Every step crosses a process boundary through the outbox, Redis Streams and inbox; messaging is registered in all five hosts.
- Attestation signing in Api and verification in Executor ([ApprovalService](../../services/Sentinel.Api/src/Sentinel.Api.Application/Approvals/ApprovalService.cs), [AttestationVerifier](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/AttestationVerifier.cs)).
- Executor's durable execution state machine with `OutcomeUnknown` reconciliation ([ExecutionRequest](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Executions/ExecutionRequest.cs)).
- Deploy-blame baseline run in shadow on every investigation; [comparison report](../../artifacts/reports/walking-skeleton/comparison.md).

Open:

- **`S1` outcome record.** The comparison report holds `S2` and `S3` only; the golden scenario's outcome and run record are missing from `artifacts/reports/walking-skeleton/`.
- **Live Muse Spark run documented with its agent-execution record.** The report states that only the deterministic test double has been used.
- **End-to-end suite as a required CI check.** Blocked on the Phase 00 CI pipeline.
- Recorded `S3` result is a wrong diagnosis with the test double. Not an exit requirement here, but it is the Phase 05 target.

## Phase 02 – Telemetry ingestion

Status: **In progress**.

Delivered:

- Providers for [Prometheus](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/PrometheusMetricsProvider.cs), [Loki](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/LokiLogsProvider.cs), [Jaeger](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/JaegerProvider.cs), the [deploy controller](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/DeployControllerProvider.cs) and a [runbook directory](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Infrastructure/Providers/RunbookDirectoryProvider.cs), with [conformance tests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/ProviderConformanceTests.cs).
- Deterministic observation identity and redaction policy in the domain; event-time watermarks and source state ([ADR-016](../decisions/ADR-016-event-time-and-late-data.md)).
- Observation query service with `ObservedThrough`, completeness and truncation metadata, behind a [query bulkhead](../../services/Sentinel.Ingestion/src/Sentinel.Ingestion.Application/QueryBulkhead.cs).
- Publication limited to metrics, deployments and topology ([ADR-018](../decisions/ADR-018-observation-publication-scope.md)).
- Ingestion telemetry for throughput, duplicates, late data, watermark lag and failures, shown in the Aspire dashboard.

Open:

- **Alert signal type.** No alert provider or observation exists; the phase lists alerts among the signals to convert.
- **Operational dashboard** (deliverable 6) exists only as Aspire's local view.
- **Exit-criteria evidence** for duplicate suppression, late-data policy, provider-outage containment and redaction parity relies on unit tests in [IngestionTests](../../services/Sentinel.Ingestion/tests/Sentinel.Ingestion.Tests/IngestionTests.cs); no recorded run.

## Phase 03 – Incident runtime

Status: **In progress**.

Delivered:

- [Grains](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Grains/IncidentGrains.cs) and [Temporal workflow and activities](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs) hosted inside Runtime ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)); Temporal and its databases in the [AppHost](../../deploy/local/Sentinel.AppHost/AppHost.cs).
- Event-sourced [incident store](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Storage/PostgresIncidentStore.cs) with optimistic concurrency, [correlation engine](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Correlation/CorrelationEngine.cs) and a projection rebuild endpoint.
- Tests for grain clustering, workflow behaviour, correlation and runtime flow under `services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/`.

Open:

- **ADR-005 acceptance tests**: worker restart, versioned upgrade of an in-flight run, and Temporal database backup and restore have no recorded evidence.
- **`S0` produces no incident** is asserted by the reference scenario tests on the SLO, not yet by a Runtime-level test that no incident opens.
- Correlation merge, split, suppression and reopen rules have no linked tests.

## Phase 04 – AI investigation

Status: **In progress**.

Delivered:

- [Specialist profiles](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/Specialists.cs) for metrics, logs, traces, changes and a generalist, with [typed tools](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Tools/InvestigationTools.cs) for metric ranges, deployments, log signatures, slow spans, dependencies and runbook search.
- [Meta model adapter](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Infrastructure/Models/MetaModelProvider.cs) and [model routing](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Models/ModelRouting.cs) with call and schema-failure telemetry.
- [Findings validation](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Application/Investigations/FindingsValidator.cs) requiring cited observations and a minimum hypothesis count; agent execution records.
- [Hypothesis blackboard](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/HypothesisBlackboard.cs) in Runtime with [tests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/BlackboardTests.cs); causal links in the projection.

Open:

- **Live model run.** No agent-execution record from Muse Spark exists in the repository.
- **Prompt-injection fixtures** embedded in logs and runbooks were not found in the test suites.
- **Model telemetry visible** is implemented but has no dashboard or recorded evidence.

## Phase 05 – Verification and consensus

Status: **In progress**.

Delivered:

- [Consensus](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/Consensus.cs) version `consensus-v1`: challenges with falsifiers and verdicts, source reliability, partial-evidence weighting, independence families, dissent margin and abstention threshold.
- [Falsification check](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Domain/Incidents/FalsificationCheck.cs) on temporal precedence and distinct support.

Open:

- **Adversarial verifier in AgentWorker.** Challenges are evaluated in Runtime; no agent role searches for contradicting evidence.
- **Calibration fixtures and reliability tracking** are not present.
- **Beating the baseline on `S1`–`S3`.** The [comparison report](../../artifacts/reports/walking-skeleton/comparison.md) shows `S3` diagnosed wrongly and `S1` absent, with the test double. This is the phase's defining exit criterion and currently has no supporting evidence.

## Phase 06 – Safe remediation

Status: **Skeleton**.

Delivered (Phase 01A stubs, kept behind their contracts):

- [Static policy](../../services/Sentinel.Executor/src/Sentinel.Executor.Domain/Policy/StaticPolicy.cs) with rule trace and emergency stop.
- Attestation verification, execution state machine, start confirmation and the [deploy-controller rollback adapter](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/ExecutorAdapters.cs) implementing `Execute`, `Inspect` and `VerifyAction`; `Compensate` is rejected by design for rollbacks.
- [Recovery verification](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/RecoveryVerificationService.cs) that is inconclusive unless `ObservedThrough` covers the window and the result is complete.

Open:

- Policy-as-code, autonomy modes, capability issuer and validator, approval expiry and revocation, signing key rotation and a managed signing facility, saga compensation, and the full Phase 06 test plan.

## Phase 07 – Memory, replay and evaluation

Status: **Skeleton**.

Delivered: exported run records and the generated [comparison report](../../artifacts/reports/walking-skeleton/README.md).

Open: incident memory, replay levels, scenario package format, postmortem generation, similar-incident retrieval, the evaluation harness and retention metadata.

## Phase 08 – Product experience

Status: **Skeleton**.

Delivered: incident list and detail pages under `apps/Sentinel.Web/src/features/incidents/`, refreshed by [polling](../../apps/Sentinel.Web/src/features/incidents/auto-refresh.tsx); approve and reject endpoints in [Api](../../services/Sentinel.Api/src/Sentinel.Api.Host/Program.cs); [development identities](../../services/Sentinel.Api/src/Sentinel.Api.Host/DevelopmentIdentity.cs).

Open: live-update channel, real identity and role-based access, evidence graph, hypothesis comparison, control-plane administration, notification routing, and the remaining journeys.

## Phase 09 – Production hardening

Status: **Not started**.

Present: resilience and health checks inside the building blocks, and the query bulkhead in Ingestion.

Open: everything in the phase, including `docs/runbooks/`, a deployment definition beyond `deploy/local/`, a threat model, Sentinel's own SLOs, and failure-injection suites. Runtime's `/admin/*` endpoints are unauthenticated until this phase ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)).

## Phase 10 – Scale and evolution

Status: **Not started**. Requires measured need and an ADR per item.

## Decisions this ledger surfaces

- Service-to-service authentication between the five services has no ADR; the [overview](../architecture/overview.md) lists it as open.
- Approver identity arrives in Phase 08 although approvals are signed from Phase 01A.
