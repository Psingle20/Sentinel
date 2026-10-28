# ADR-017: Runtime actor and workflow hosting

- Status: Accepted
- Date: 2026-10-28

## Context

Phase 03 moves the incident's single writer into `IncidentGrain` ([ADR-004](ADR-004-runtime-state-ownership.md)) and its step progress into a Temporal workflow ([ADR-005](ADR-005-durable-workflow-engine.md)). ADR-005 asked for Orleans and Temporal hosting to be recorded together: where the silo's membership lives, how grain calls are serialized, how one incident maps to workflow runs, what crosses the workflow boundary, and how the workflow changes safely while runs are in flight.

## Constraints

- No new Sentinel deployable: silo and workflow worker run inside `Sentinel.Runtime` ([ADR-001](ADR-001-production-first-service-topology.md)).
- No custom infrastructure; reuse what local orchestration already runs ([ADR-008](ADR-008-event-transport.md), [ADR-009](ADR-009-persistence-baseline.md), [ADR-010](ADR-010-local-orchestration.md)).
- The domain and contracts stay free of actor-runtime and workflow SDKs; the application layer depends on ports only.
- Workflow code performs no I/O, and the incident stream stays the truth.

## Considered options

1. Orleans membership in the messaging Redis, grain payloads as contract JSON, one workflow run per incident cycle, commands as serializable data.
2. Orleans membership in PostgreSQL through ADO.NET clustering, grain payloads with generated Orleans serializers on application types.
3. Localhost clustering only, with one Runtime instance.

## Decision

Adopt option 1.

**Actors.**

- `IncidentGrain` (keyed by incident ID) is the only writer of an incident's stream. It keeps the rehydrated aggregate between commands, drops it after any failure, and re-applies a command once more when the store's optimistic append reports a conflict.
- `CorrelationGrain` (keyed `tenant/environment`) runs detection and correlation for its partition one observation at a time, so one failure can never open two incidents.
- `ServiceGrain` (keyed `tenant/environment/service`) caches catalog facts and observed dependency edges and is never authoritative.
- Grains reach the outside world only through Runtime's own stores and other grains. Grains are non-reentrant, and no grain calls back into a grain that calls it: `CorrelationGrain` calls `IncidentGrain`, never the reverse.
- Membership is kept in the messaging Redis (`Microsoft.Orleans.Clustering.Redis`). Development can run `Runtime:Orleans:Clustering=localhost`. Silo port 11111 and gateway port 30000 are configurable.
- Grain-call payloads are Sentinel types serialized by the Orleans System.Text.Json codec with the contract JSON rules ([ADR-012](ADR-012-event-semantics.md)), so application records need no Orleans attributes.
- Commands to the writer are serializable data (`IncidentCommand`). Any I/O a command depends on happens before it is sent: policy evaluation, change lookups and recovery measurements.
- Every command is idempotent. IDs that a retried step might regenerate are derived deterministically: investigations, decisions and plans from incident, cycle and step; incidents from the triggering observation.

**Workflows.**

- The workflow is `sentinel.incident` on task queue `sentinel-runtime`, with ID `incident-{id}-cycle-{n}` and reuse policy `RejectDuplicate`. Starting is therefore idempotent, a finished cycle never runs again, and a reopened incident runs its next cycle under a new ID. `WorkflowReconciler` restarts missing runs for open incidents every minute.
- Activities pass incident IDs and receive a `StepSummary` of identifiers and states. Timings are fixed in the run's input so a replay sees the same values.
- Inbound messages are recorded first through the writer, then signalled. A signal is a hint: before acting, the workflow re-reads the incident.
- Waits are signals with timeouts: investigations, approval expiry and execution result. The recovery window is a durable timer.
- Compensation: a cancelled run escalates the incident through a detached activity. Runtime then refuses the start confirmation for any requested but unstarted execution ([ADR-006](ADR-006-remediation-control-path.md)).
- Changes that alter the command sequence of in-flight runs are guarded by `Workflow.Patched` with a named marker, each covered by an upgrade test. The first marker is `reread-before-approval-wait`.
- Runtime is ready only when its worker polls Temporal. A lost connection restarts the worker with backoff.
- Local orchestration runs `temporalio/auto-setup:1.28.1` against Sentinel's PostgreSQL server, in the databases `temporal` and `temporal_visibility`. No Sentinel service reads them.

## Consequences

### Positive

- One writer per incident without locks or an extra coordination store; a lost silo costs only a reactivation.
- Membership and grain payloads reuse infrastructure and rules that already exist.
- Retries, redeliveries and duplicate activities converge on the same stream.
- Every Temporal acceptance test in ADR-005 runs against the image local orchestration uses.

### Negative

- Redis membership ties silo discovery to the messaging Redis; losing it stops new silos joining, though running silos keep serving.
- JSON grain payloads are larger and slower than generated serializers; acceptable at incident command rates.
- Every in-flight-changing workflow edit needs a patch marker and an upgrade test.

## Follow-up work

- Phase 09: put the operator endpoints (`/admin/*`) behind operator authorization; size silo and worker counts from load tests.
- Phase 10: revisit Redis membership if Runtime runs across zones.

## Revisit criteria

Revisit if grain payload serialization shows up in profiles, if `IncidentGrain` contention is measured (ADR-004), or if Temporal's operational cost outweighs its semantics (ADR-005).
