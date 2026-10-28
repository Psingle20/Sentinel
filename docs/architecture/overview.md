# Sentinel architecture overview

## Product outcome

Sentinel acts as an evidence-driven incident-response team for distributed systems. It correlates operational signals, investigates them through specialist tools, constructs competing hypotheses, challenges the leading explanation, proposes a safe response, and verifies recovery.

## Golden vertical slice

The first executable scenario is intentionally narrow:

1. A checkout-service deployment introduces a Redis connection leak.
2. Connection growth raises checkout latency and causes retry amplification.
3. Sentinel correlates SLO degradation, telemetry, and the recent deployment into one incident.
4. Meta-powered investigators inspect logs, metrics, traces, and deployment history through typed tools.
5. Evidence is stored with provenance and connected to competing hypotheses.
6. An adversarial pass attempts to falsify the leading hypothesis.
7. Sentinel produces an evidence-backed diagnosis and a structured rollback plan.
8. Policy requires human approval before the constrained rollback adapter runs.
9. Sentinel verifies recovery against latency, error-rate, connection, and SLO signals.
10. The incident timeline can be replayed for operational review and evaluation.

The same reference system also runs confounder scenarios in which the most recent deployment is innocent, or the causal direction is reversed. Sentinel's diagnosis is always compared with a deterministic deploy-blame baseline ([Phase 01](../implementation/phase-01-reference-system.md#scenario-suite)).

## Production deployment boundaries

```text
Sentinel.Web -> Sentinel.Api -> Sentinel.Runtime
                                  ^       |
Sentinel.Ingestion ---------------'       v
        ^                         Sentinel.AgentWorker
        `--- observation queries ---------'

Sentinel.Web -> Sentinel.Api -> signed approval -> Sentinel.Runtime
                                                       |
                         execute(plan, attestation) ---'
                                     |
                                     v
                             Sentinel.Executor
                                     |
                                     `-> start confirmation, result -> Sentinel.Runtime
```

Approval, execution and recovery follow [ADR-006](../decisions/ADR-006-remediation-control-path.md). Provider access follows [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md).

Sentinel begins with five backend processes: API, ingestion, runtime, agent worker, and executor. They are deliberately coarse-grained production boundaries. Internal modules preserve clean separation without turning every module into a network service.

`Sentinel.Web` uses Next.js, React and TypeScript with Tailwind CSS, shadcn/ui and Radix UI. A governed [frontend design system](frontend-design-system.md) constrains tokens, variants, components and exceptions so multiple contributors preserve one operational design language.

## Delivery invariant

Build one vertical feature through all five backend services before expanding any individual service. The first slice is the golden incident path above, delivered as the [Phase 01A walking skeleton](../implementation/phase-01a-walking-skeleton.md). This controls sequencing without removing later capabilities; the implementation [coverage matrix](../implementation/coverage-matrix.md) accounts for the complete architecture.

## Stable contracts

The core model owns canonical types for observations, incidents, evidence, hypotheses, decisions, remediation plans, approvals, executions, and recovery checks. Provider-specific payloads are translated at integration boundaries.

AI conclusions are not authoritative by themselves. Evidence records must retain source observation identifiers, time ranges, collection tool calls, direction, strength, and provenance. Privileged actions require typed plans and policy evaluation.

## Impact-ordered evolution

This list mirrors the [implementation phases](../implementation/README.md).

1. Scaffold all five backend services, the web frontend and their production boundaries (Phase 00).
2. Build the reference system with the golden scenario, confounders and the deploy-blame baseline (Phase 01).
3. Complete one narrow path across observation, incident, Meta investigation, approval, rollback and measured recovery: the walking skeleton (Phase 01A).
4. Deepen ingestion to every signal type with event-time handling and the observation query contract (Phase 02).
5. Replace the skeleton runtime with Orleans ownership, event-sourced decision history and durable workflows (Phase 03).
6. Expand multi-source evidence collection and inspectable hypotheses (Phase 04).
7. Deepen adversarial falsification and evidence-backed consensus until Sentinel beats the baseline (Phase 05).
8. Expand policy, remediation and recovery capabilities (Phase 06).
9. Add incident memory, deterministic replay and evaluation (Phase 07), then the full product experience (Phase 08).
10. Harden the complete loop (Phase 09); add providers, multi-tenancy at scale and advanced autonomy only on measured need (Phase 10).

## Trust boundaries

- Telemetry and retrieved text are untrusted input.
- Model outputs are untrusted proposals until schema validation and policy checks succeed.
- Agents receive read capabilities and narrowly scoped action descriptions, not broad production credentials.
- Only `Sentinel.Ingestion` holds external provider credentials; agents read through its query contract after redaction.
- Approvals are signed by `Sentinel.Api` and verified by `Sentinel.Executor`; `Sentinel.Runtime` relays them but cannot create them.
- The remediation broker is the only component permitted to invoke action adapters.
- Recovery is determined from expected system signals, not merely a successful action API response.

## Decisions

Accepted:

- Backend platform: .NET 10 LTS ([ADR-003](../decisions/ADR-003-dotnet-backend-platform.md)).
- Runtime state ownership: the event stream is authoritative ([ADR-004](../decisions/ADR-004-runtime-state-ownership.md)).
- Durable workflow engine: Temporal ([ADR-005](../decisions/ADR-005-durable-workflow-engine.md)).
- Remediation control path ([ADR-006](../decisions/ADR-006-remediation-control-path.md)).
- Cross-service code and provider access ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)).
- Event transport: Redis Streams ([ADR-008](../decisions/ADR-008-event-transport.md)).
- Persistence: PostgreSQL, one database per service ([ADR-009](../decisions/ADR-009-persistence-baseline.md)).
- Local orchestration: .NET Aspire ([ADR-010](../decisions/ADR-010-local-orchestration.md)).
- Reference system in `reference-system/` ([ADR-011](../decisions/ADR-011-reference-system-location.md)).
- Event semantics and message envelope ([ADR-012](../decisions/ADR-012-event-semantics.md)).
- Building-block reuse ([ADR-013](../decisions/ADR-013-building-block-reuse.md)), provider abstraction ([ADR-014](../decisions/ADR-014-provider-abstraction.md)) and model abstraction ([ADR-015](../decisions/ADR-015-model-abstraction.md)).
- Event time, watermarks and late data ([ADR-016](../decisions/ADR-016-event-time-and-late-data.md)).
- Orleans actor and Temporal workflow hosting inside `Sentinel.Runtime` ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)).
- Observation publication scope: metrics, deployments and topology on the stream; logs and traces through the query contract ([ADR-018](../decisions/ADR-018-observation-publication-scope.md)).

Still open:

- Deployment environment for the initial product release; only the Aspire local orchestration exists (Phase 09).
- Service-to-service authentication between the five backend services. Approval attestations are covered by ADR-006; the execute command, start confirmation and observation queries have no recorded decision yet.
- Approver identity before Phase 08; the walking skeleton uses local development identities.

Delivery status against the phases is tracked in the [phase status ledger](../implementation/phase-status.md).
