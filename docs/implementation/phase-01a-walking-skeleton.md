# Phase 01A – Walking skeleton

## Outcome

Close the complete Sentinel loop once, end to end, across all five backend services and the web frontend, using the simplest honest implementation at each step. This phase is the first delivery of the non-negotiable rule: one vertical feature through all five services before any service gains breadth.

Phases 02–08 each deepen one part of this path, replace the stubs named below and keep the path running.

## Primary service ownership

All five backend services and `Sentinel.Web`. Each implements only its step of the golden path. The [Phase 01 reference system](phase-01-reference-system.md) supplies the environment, the scenario manifests and the deploy-blame baseline definition.

## Prerequisites

- Phase 00 hosts, contracts, local orchestration, CI and the vertical-slice frontend components.
- Phase 01 scenarios `S0`–`S3` and the baseline definition.
- The persistence-baseline and event-semantics ADRs from Phase 00.
- [ADR-003](../decisions/ADR-003-dotnet-backend-platform.md), [ADR-004](../decisions/ADR-004-runtime-state-ownership.md), [ADR-006](../decisions/ADR-006-remediation-control-path.md) and [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md). Temporal ([ADR-005](../decisions/ADR-005-durable-workflow-engine.md)) is not used until Phase 03.

## The path

| Step | Service | Skeleton implementation | Deepened in |
| --- | --- | --- | --- |
| Observe | `Sentinel.Ingestion` | One pull adapter for checkout SLO and Redis connection metrics (labeled by client name), plus deployment events from the reference deploy controller. Emits canonical `MetricObservation` and `DeploymentObservation` with deterministic IDs. Serves the minimal observation query contract (ADR-007). | [02](phase-02-telemetry-ingestion.md) |
| Correlate | `Sentinel.Runtime` | One rule: checkout SLO burn above the manifest threshold opens an incident and links deployments to the affected service or its dependencies inside a fixed lookback window. Incident state is an append-only, versioned table with one in-process writer (ADR-004). No Orleans, event-sourcing framework or workflow engine. | [03](phase-03-incident-runtime.md) |
| Investigate | `Sentinel.AgentWorker` | One generalist investigator: a Muse Spark tool-call loop through `IModelProvider` with two typed read tools, metric range and recent deployments, served by Ingestion. Output is schema-validated evidence citing observation IDs plus at least two hypotheses; invalid output leads to abstention. The deploy-blame baseline runs behind the same investigator contract. | [04](phase-04-ai-investigation.md) |
| Challenge and decide | `Sentinel.Runtime` | One deterministic falsification check. The leading hypothesis's cause must precede symptom onset in event time, and its support must cite at least two distinct observations; otherwise Runtime abstains. Writes a decision record. | [05](phase-05-verification-consensus.md) |
| Plan | `Sentinel.Runtime` | One plan template: roll the implicated service back one revision. The plan carries a version and content hash, and Runtime requests a plan-time policy evaluation from Executor. | [06](phase-06-safe-remediation.md) |
| Approve | `Sentinel.Web`, `Sentinel.Api` | Incident list and detail page (timeline, evidence, hypotheses, decision, plan) built from Phase 00 components, refreshed by polling. Approving sends the plan hash to Api, which signs an attestation and forwards it to Runtime (ADR-006). Local development identities only. | [08](phase-08-product-experience.md) |
| Act | `Sentinel.Executor` | Static policy: rolling back a reference-system service by one revision requires approval; everything else is denied. Verifies the attestation, plan hash and fencing token, obtains start confirmation, runs one rollback adapter against the reference deploy controller, and verifies that the target revision is serving. Each request is tracked in Executor's durable execution state machine (ADR-006); an unknown outcome is reconciled by inspecting the serving revision, never by repeating the rollback. | [06](phase-06-safe-remediation.md) |
| Verify | `Sentinel.Runtime` | After the manifest's observation window, queries SLO burn and Redis connections through Ingestion and compares them with the manifest thresholds. The result is inconclusive unless the query's `ObservedThrough` covers the whole window and the result is complete (ADR-007). Resolves, escalates or reports inconclusive. | [06](phase-06-safe-remediation.md) |
| Present and record | `Sentinel.Api`, `Sentinel.Web`, `Sentinel.Runtime` | Final timeline from Runtime's projection, served through Api. Each run exports a run record (timeline, evidence, decision, plan, execution and recovery) for evaluation. | [07](phase-07-memory-replay-evaluation.md), [08](phase-08-product-experience.md) |

Each stub sits behind the contract its deepened version will use. Later phases replace implementations, not contracts, unless a contract change is versioned and documented.

## Messaging and persistence

- Every step crosses a real process boundary over the transport selected in Phase 00.
- State changes and outgoing messages use the simplest correct pattern for that transport; a single outbox table is acceptable. Full outbox, inbox and dead-letter tooling arrives in Phases 02 and 03.
- Handlers are idempotent on deterministic keys, so duplicate delivery never creates a second incident, plan or execution.

## Primary deliverables

1. End-to-end golden path for `S1` across all five services and the web frontend.
2. Minimal observation query contract, investigator contract, plan contract, approval attestation and execution contracts in `Sentinel.Contracts`.
3. Deploy-blame baseline investigator behind the investigator contract, selectable by configuration.
4. One command that runs a scenario from healthy state through break, detection, investigation, approval, rollback, verification and reset.
5. End-to-end suite in CI using the model test double, with Playwright performing the approval.
6. First comparison report: skeleton and baseline outcomes for `S1`–`S3`.

## Test plan

- Playwright golden journey from incident list to verified recovery.
- Contract tests at every service boundary.
- Duplicate-delivery tests for each message on the path.
- Approval negative tests: missing, expired, wrong plan hash, forged and replayed attestation.
- Restart of each service between steps without loss of accepted state or a repeated rollback.
- Executor killed while executing: the request ends in `OutcomeUnknown` and is reconciled from the target's serving revision.
- Recovery check against delayed telemetry: the result is inconclusive, not recovered.
- Model test double returning malformed and incomplete output, which must lead to abstention.

## Exit criteria

- `S1` runs end to end across real process boundaries with no database or terminal intervention.
- No service reads another service's storage, and AgentWorker holds no provider credentials.
- Duplicate delivery and single-service restarts do not duplicate incidents, plans or rollbacks.
- The rollback cannot execute without a valid attestation for the exact plan hash.
- The CI run is deterministic with the model test double. A live Muse Spark run is documented with its agent-execution record.
- `S2` and `S3` run through decision and plan, with no scripted approval. Their skeleton and baseline outcomes are recorded but are not exit requirements.
- The end-to-end suite is a required CI check for every later phase.

## Risks and controls

| Risk | Control |
| --- | --- |
| Skeleton code hardens into the product | Every stub is named in the table above. The deepening phase's exit criteria require replacing it. |
| Transport and orchestration work take longer than expected | Time-box them. This is the early cost ADR-001 accepted, and it is cheaper to discover now than after the services have grown. |
| The path "works" only because `S1` is easy | `S2` and `S3` outcomes and the baseline are recorded from the first run. |

## Deferred deliberately

Orleans, event sourcing, the workflow engine, logs, traces, topology, specialist agents, adversarial verification, consensus scoring, policy-as-code, live UI updates, multi-tenancy beyond carrying identifiers, incident memory and replay. Each has a destination phase in the table above or in the [coverage matrix](coverage-matrix.md).
