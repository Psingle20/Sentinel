# ADR-004: Runtime state ownership

- Status: Accepted
- Date: 2026-10-27

## Context

Phase 03 introduces Orleans grains, an event-sourced incident stream and a durable workflow. As drafted, `IncidentGrain` owns the incident lifecycle, `InvestigationGrain` owns active investigation state, the workflow owns step progress and the event stream is the durable history. Three components therefore hold a version of "where is this investigation", and nothing states which one wins when they disagree after a crash, a retry or a superseded decision.

## Constraints

- `Sentinel.Runtime` is the single source of truth for incident state, evidence, hypotheses, decisions and workflow progress ([ADR-001](ADR-001-production-first-service-topology.md)).
- Decisions must be replayable from history (architecture §10 and Phase 07).
- Stale owners or workers must not cause irreversible effects (Phases 03 and 06).
- The rule must hold whichever workflow engine is used; [ADR-005](ADR-005-durable-workflow-engine.md) selects Temporal.

## Considered options

1. Each component owns the state it touches (the current draft).
2. The workflow owns all incident state; grains and events are derived from it.
3. The event stream owns domain truth, `IncidentGrain` is its only writer, and the workflow owns progress only.

## Decision

Adopt option 3: **the event stream records what is true; the workflow decides what happens next.**

| Concern | Owner | Rule |
| --- | --- | --- |
| Incident and investigation state, accepted evidence, hypothesis status, decisions, plans, approvals received, execution results and recovery outcome | Incident event stream | Authoritative. Appended only by `IncidentGrain` with optimistic concurrency on the stream version. |
| Serialized command handling and invariants | `IncidentGrain` | The only writer of an incident stream. Rehydrates from the stream and holds no state that cannot be derived from it. |
| Step progress, waits, timers, retries, cancellation and compensation scheduling | Durable workflow | Holds no copy of domain state. Reads state by querying `IncidentGrain` and changes it only by sending commands to `IncidentGrain`, always through the engine's side-effect boundary, never from orchestration code directly. For Temporal, that boundary is an activity ([ADR-005](ADR-005-durable-workflow-engine.md)). |
| Correlation decisions for a partition | `CorrelationGrain` | Owns correlation state for its partition and sends open, link and merge commands to `IncidentGrain`. |
| Hot service context | `ServiceGrain` | Cache only; never authoritative. |
| Queries | Read projections | Derived and rebuildable from the stream. |

Investigation state is modeled inside the incident stream. `InvestigationGrain` is deferred until one incident needs several concurrently owned investigations or `IncidentGrain` becomes a measured hotspot; introducing it amends this ADR.

When the workflow and the stream disagree, the stream wins. Workflow activities send idempotent commands keyed by incident ID, workflow run ID and step, so a resumed or duplicated step either appends nothing new or is rejected by the version or idempotency check.

Every command that can lead to an external effect carries the stream version it was based on. That version is the fencing token `Sentinel.Executor` checks under [ADR-006](ADR-006-remediation-control-path.md).

The [Phase 01A walking skeleton](../implementation/phase-01a-walking-skeleton.md) applies the same rule without Orleans or a workflow engine: one append-only, versioned incident table owned by `Sentinel.Runtime` and written by one in-process handler. Phase 03 moves the single writer into `IncidentGrain` and the progress logic into the workflow without changing the rule.

## Consequences

### Positive

- One answer to "what is true" after any crash, retry or replay.
- Domain replay (Phase 07, level 1) needs only the event stream.
- The workflow engine can be replaced without migrating domain state.

### Negative

- Workflow code cannot cache domain state for convenience; it queries the grain, which adds calls.
- One grain per incident serializes all of that incident's commands; very busy incidents may need the deferred split.

## Follow-up work

- Update the Phase 03 ownership model and initial grain list.
- Define the incident event envelope (stream ID, version, causation and correlation IDs, schema version) in the Phase 00 event-semantics ADR.
- Add architecture tests that prevent workflow code from reading or writing domain persistence directly, or calling grains outside an activity.

## Revisit criteria

Revisit when measurements show `IncidentGrain` contention, when an incident needs parallel independently owned investigations, or when the workflow engine cannot operate with external state ownership.
