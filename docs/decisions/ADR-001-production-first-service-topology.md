# ADR-001: Production-first five-service topology

- Status: Accepted
- Date: 2026-10-25

## Context

Sentinel needs independently scalable ingestion, stateful incident coordination, model-intensive investigation and privileged remediation. Those workloads have different trust boundaries, failure modes, resource profiles and release concerns. A single backend would reduce initial deployment work but postpone validation of the boundaries that matter most in production.

At the same time, creating many narrow services would spend the early delivery window on infrastructure and distributed coordination while leaving the incident-response loop incomplete.

## Constraints

- The frontend remains an application under `apps/`; backend deployables live under `services/`.
- Models never receive privileged remediation credentials.
- Services do not share database ownership or access each other's schemas.
- Every architecture capability must remain assigned to a delivery phase.
- The first usable result must be a complete incident path, not a collection of deep but disconnected services.

## Considered options

1. One backend modular monolith and one frontend.
2. Five coarse-grained backend services and one frontend from the start.
3. A fine-grained service per logical module.

## Decision

Use five backend services from the start:

- `Sentinel.Api`
- `Sentinel.Ingestion`
- `Sentinel.Runtime`
- `Sentinel.AgentWorker`
- `Sentinel.Executor`

Use `Sentinel.Web` as the frontend application. Preserve clean modules inside each service and stable shared contracts in packages.

Delivery follows one non-negotiable rule:

> Build one vertical feature through all five backend services before expanding any individual service.

The initial feature is the golden path from canonical observation through correlation, Meta-powered investigation, adversarial decision, human approval, constrained rollback and telemetry-based recovery verification.

## Consequences

### Positive

- Trust boundaries and production communication paths are exercised immediately.
- Ingestion, actor runtime, model work and execution can scale and fail independently.
- Privileged remediation remains isolated from models and public APIs.
- The repository communicates intended ownership unambiguously.

### Negative

- Local orchestration, transport, tracing and contract testing are required earlier.
- Cross-service failures and eventual consistency appear in the first slice.
- Developers must maintain strict data ownership and compatibility discipline from the start.

## Guardrails

- Start each service with only the behavior required by the current vertical slice.
- Prefer asynchronous, durable messages for long-running transitions.
- Require idempotency, correlation/causation identifiers and schema compatibility at boundaries.
- Permit a shared database server initially, but never shared tables, credentials or persistence contexts.
- Keep service-specific code in its service; share only stable contracts and genuinely reusable primitives.
- Add `Sentinel.ReplayWorker` or another deployable only after measured isolation, scale or ownership evidence and a new ADR.
- Use the implementation coverage matrix as the no-omissions check.

## Follow-up work

- Scaffold the five services and frontend in Phase 00.
- Record transport, persistence and local orchestration choices in focused ADRs.
- Add architecture tests and contract tests that enforce the boundaries.
- Validate the complete golden vertical slice before adding provider, agent, policy or UI breadth.

## Revisit criteria

Revisit the topology when measurements show that a boundary should merge or split, when ownership changes materially, or when operational cost exceeds its isolation benefit. Any change must preserve the trust boundaries and include a migration and rollback plan.

