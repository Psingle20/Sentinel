# Production topology and vertical-slice delivery

## Decision

Sentinel starts with five independently deployable backend services and one frontend. This is the production topology, not a temporary prototype layout.

```text
apps/
└── Sentinel.Web

services/
├── Sentinel.Api
├── Sentinel.Ingestion
├── Sentinel.Runtime
├── Sentinel.AgentWorker
└── Sentinel.Executor
```

An optional `Sentinel.ReplayWorker` may be introduced later when replay load, isolation, or release cadence justifies another deployment. It is not part of the initial five-service topology.

## Non-negotiable delivery rule

> Build one vertical feature through all five backend services before expanding any individual service.

The first feature is the golden incident path, delivered as the [Phase 01A walking skeleton](phase-01a-walking-skeleton.md). Each service implements only the minimum behavior needed to make that path real. Breadth inside a service is deferred until the complete path passes its end-to-end acceptance criteria.

This rule controls delivery order; it does not reduce architectural scope. The [coverage matrix](coverage-matrix.md) remains the completeness ledger for all planned capabilities.

## Service ownership

| Deployable | Owns | Does not own |
| --- | --- | --- |
| `Sentinel.Api` | External HTTP/API boundary, identity and authorization, control-plane commands, incident queries, approval capture and attestation signing, live UI delivery | Telemetry normalization, authoritative incident state, model execution, privileged remediation, commands to Executor |
| `Sentinel.Ingestion` | Provider connections and credentials, canonical normalization, event-time metadata, checkpoints, deduplication, durable publication, read-only observation queries | Incident decisions, AI conclusions, remediation |
| `Sentinel.Runtime` | Authoritative incident state, correlation, Orleans actors, durable workflows, evidence and hypotheses, consensus, projections, recovery verification | Provider credentials, direct model SDK use, privileged action execution |
| `Sentinel.AgentWorker` | Meta model calls, typed read tools over the Ingestion query contract, specialist investigations, adversarial verification, structured findings | Authoritative incident writes, approvals, provider or production credentials, direct remediation |
| `Sentinel.Executor` | The policy engine, capabilities, privileged credentials, action adapters, compensation and action verification | Diagnosis, free-form model decisions, UI approval capture, the recovery decision |
| `Sentinel.Web` | Responder and administrator experience, governed design system and live presentation state | Business authority, direct service/database access, or feature-specific visual languages |

## End-to-end communication

```text
Sentinel.Web
    -> Sentinel.Api
        -> Sentinel.Runtime

Sentinel.Ingestion
    -> canonical observations
        -> Sentinel.Runtime

Sentinel.Runtime
    -> investigation task
        -> Sentinel.AgentWorker
            -> observation queries -> Sentinel.Ingestion
            -> structured findings
                -> Sentinel.Runtime

Sentinel.Runtime
    -> plan-time policy evaluation <-> Sentinel.Executor

Sentinel.Web
    -> approve(plan hash)
        -> Sentinel.Api (signs attestation)
            -> Sentinel.Runtime
                -> execute(plan, attestation, fencing token)
                    -> Sentinel.Executor
                        -> start confirmation <-> Sentinel.Runtime
                        -> execution and action-verification result
                            -> Sentinel.Runtime
                                -> recovery verification
                                -> projection/live update
                                    -> Sentinel.Api -> Sentinel.Web
```

Approval, execution and recovery follow [ADR-006](../decisions/ADR-006-remediation-control-path.md). Provider access follows [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md).

Long-running work crosses service boundaries asynchronously. Synchronous calls are reserved for bounded request/response operations. Contracts are schema-versioned, idempotent where applicable, and carry tenant, environment, service, incident, correlation and causation identity.

## Data ownership

- Each service owns its state and schema. No service reads or writes another service's tables.
- Services may initially share one PostgreSQL server for operational simplicity, but use separate databases or schemas, credentials and persistence contexts.
- `Sentinel.Runtime` is the source of truth for incident state, evidence, hypotheses, decisions and workflow progress.
- `Sentinel.Ingestion` owns provider checkpoints and delivery state.
- `Sentinel.AgentWorker` owns execution metadata needed for model/tool auditing; accepted evidence becomes Runtime-owned state.
- `Sentinel.Executor` owns policy/execution audit, capability consumption and adapter results.
- `Sentinel.Api` owns control-plane configuration only where another service is not the authority; read views remain derived and rebuildable.
- Workflow-engine persistence ([ADR-005](../decisions/ADR-005-durable-workflow-engine.md)) belongs to that infrastructure; no Sentinel service reads it directly, and it never holds domain state ([ADR-004](../decisions/ADR-004-runtime-state-ownership.md)).

## Packages versus services

Deployable behavior belongs under `services/`; the UI belongs under `apps/`. `packages/` is limited to stable reusable code with named consumers, such as:

```text
packages/
├── CleanArchitecture.BuildingBlocks   Core, Application, Hosting
├── Sentinel.Contracts                 Sentinel.Contracts.Primitives, Sentinel.Contracts
└── Sentinel.ServiceDefaults
```

There is no shared domain package. Each service owns its domain module, and services share types only through `Sentinel.Contracts` and `Sentinel.Contracts.Primitives`; a domain module may reference only the primitives. Provider and model interfaces stay inside their owning services until a second concrete consumer exists ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)). Service-specific application logic, persistence, adapters and hosts remain inside their owning service. Shared packages must not become a distributed monolith that allows services to reach into each other's internals.

The frontend design system remains inside `apps/Sentinel.Web` until another real frontend consumer exists. It is not placed in `packages/` merely for organizational symmetry.

## First vertical slice

The first slice proves one controlled incident from observation to verified recovery. [Phase 01A](phase-01a-walking-skeleton.md) defines each step's skeleton implementation and the phase that deepens it.

1. `Sentinel.Ingestion` accepts one provider's checkout signals and emits canonical observations.
2. `Sentinel.Runtime` correlates them with one deployment and opens one incident.
3. `Sentinel.AgentWorker` uses one Meta model, reading through the Ingestion query contract, to return attributable evidence and competing hypotheses.
4. `Sentinel.Runtime` challenges the leader, records a decision and creates one typed rollback plan.
5. `Sentinel.Web` retrieves the incident through `Sentinel.Api`; an operator approves the immutable plan, and Api sends a signed attestation to Runtime.
6. `Sentinel.Runtime` sends the execute command to `Sentinel.Executor`, which validates policy, attestation and fencing, obtains start confirmation, runs one constrained rollback adapter and reports the result.
7. `Sentinel.Runtime` verifies recovery from new observations and projects the final timeline back through the API to the UI.

The slice is complete only when it runs across real process boundaries, survives safe retries, preserves one audit trail, and can be operated without direct database or terminal intervention.

## Phase ownership map

| Phase | Primary deployables | Cross-service result |
| --- | --- | --- |
| 00 – Foundations | All five services + Web | Hosts, contracts, transport, observability and local orchestration work together |
| 01 – Reference system | Reference environment, outside Sentinel service count | Deterministic failures, confounders, baseline and reset contract exist |
| 01A – Walking skeleton | All five services + Web | The golden path runs end to end with the simplest honest implementation at each step |
| 02 – Ingestion | Ingestion; Runtime consumes; API configures | Canonical observations reach the incident boundary |
| 03 – Runtime | Runtime; Ingestion produces; API exposes | One durable incident is correlated and queryable |
| 04 – AI investigation | AgentWorker; Runtime dispatches/accepts; API configures | Structured findings become attributable evidence |
| 05 – Verification | AgentWorker challenges; Runtime decides | Consensus or abstention is authoritative and inspectable |
| 06 – Remediation | Executor; API approves; Runtime orchestrates | One policy-gated action is executed and recovery is measured |
| 07 – Memory/replay | Runtime + AgentWorker; API exposes | Incident history is replayable; ReplayWorker remains optional |
| 08 – Product experience | Web + API; all services publish status | Full responder journey works without back doors |
| 09 – Hardening | All five services + Web | Security, resilience and operations cover every boundary |
| 10 – Scale/evolution | Evidence-driven changes across all deployables | Cells, regions, providers and autonomy expand without weakening ownership |

## Completeness controls

- No phase may delete an architecture capability; deferrals must name the destination phase.
- Every architecture section remains mapped in the [coverage matrix](coverage-matrix.md).
- Every phase states primary service ownership and a cross-service exit condition.
- A service may gain breadth only after the currently selected vertical slice works through all five services.
- New deployables require an ADR that identifies ownership, data, failure isolation, scaling evidence and operational cost.

