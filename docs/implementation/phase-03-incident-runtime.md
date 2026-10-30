# Phase 03 – Incident intelligence and durable runtime

## Outcome

Transform canonical observations into a correlated, durable and queryable incident lifecycle with a single owner, replayable decisions and resumable orchestration.

## Primary service ownership

`Sentinel.Runtime` owns authoritative incident state and workflow progress. `Sentinel.Ingestion` supplies observations and `Sentinel.Gateway` exposes commands and queries. Neither may read or write Runtime-owned storage directly.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) append-only incident table, in-process handler and single correlation rule with `IncidentGrain`, the event-sourced stream, the durable workflow and the correlation engine. State ownership follows [ADR-004](../decisions/ADR-004-runtime-state-ownership.md). Durable workflows use Temporal under [ADR-005](../decisions/ADR-005-durable-workflow-engine.md) and its implementation constraints. The end-to-end suite must pass with the new runtime on the path.

## In scope

- Incident detection from SLO burn, alerts and selected anomaly/rule signals.
- Correlation using tenant, environment, service topology, temporal proximity, trace links and recent changes.
- Service catalog, ownership, dependency and SLO context needed by correlation.
- Incident, investigation and service lifecycle state machines.
- Orleans grains where virtual actors provide real ownership/concurrency value: initially `IncidentGrain`, `ServiceGrain` and `CorrelationGrain`. Investigation state lives in the incident stream; `InvestigationGrain` is deferred under ADR-004.
- Event-sourced incident and decision history with optimistic concurrency.
- CQRS read projections for incident list, detail, timeline, affected services and current phase.
- Durable investigation workflow on Temporal, with checkpoints, timeouts, retry policy and cancellation. The workflow holds progress only, performs no I/O, and reaches `IncidentGrain` only through activities.
- Specialist fan-out as parallel activities; child workflows only where an investigator needs an independent lifecycle, history or worker ownership.
- Activity payloads limited to IDs and small summaries.
- Temporal server and its PostgreSQL database in local orchestration.
- Saga/compensation definitions for workflow steps that create external effects.
- Correlation merge, split, suppression and reopen rules.
- Severity and prioritization derived from SLO impact and blast radius.

## Ownership model

```text
Event stream           authoritative incident, investigation and decision state
IncidentGrain          only writer of its stream; serializes commands and invariants
CorrelationGrain       serializes correlation decisions for its partition
ServiceGrain           caches hot runtime service context; never authoritative
Workflow               owns step progress, waits, timers, retry and compensation;
                       reads via IncidentGrain, changes state only by command
Read projections       serve queries; they are rebuildable
```

When the workflow and the stream disagree, the stream wins ([ADR-004](../decisions/ADR-004-runtime-state-ownership.md)). Grains coordinate domain state but do not call vendor APIs directly.

## Primary deliverables

1. Incident domain model, commands, queries, events and state transitions.
2. Correlation engine with explainable scores and linked observations.
3. Orleans runtime, persistence and membership configuration for the selected environment.
4. Event store and projection pipeline with rebuild tooling.
5. Durable investigation workflow and failure recovery.
6. Timeline and incident query APIs usable by later UI phases.

## Consistency and concurrency

- One authoritative incident owner processes lifecycle commands serially.
- Every mutation carries an expected version or idempotency key.
- Event publication uses an outbox when persistence and publishing cannot be atomic.
- Projection lag is visible; query responses distinguish authoritative state from projections when relevant.
- Stale owners or workers cannot execute irreversible effects.

## Test plan

- Domain transition and invariant tests.
- Correlation fixture tests, including unrelated simultaneous failures.
- Orleans activation, deactivation, reentrancy and node-loss tests.
- Event-stream replay and projection rebuild tests.
- Workflow crash/restart, timeout, cancellation and compensation tests.
- Duplicate command and concurrency-conflict tests.
- Temporal acceptance tests from [ADR-005](../decisions/ADR-005-durable-workflow-engine.md#phase-03-acceptance-tests): worker restart, approval signals, duplicate activities, Runtime and Temporal failures, versioned upgrades of in-flight runs, and backup and restore of the Temporal database.
- Architecture tests rejecting I/O or grain calls in workflow code.

## Exit criteria

- Golden-scenario observations produce exactly one explainable incident, and `S0` produces none.
- The incident survives process and Orleans-node restarts without losing accepted state.
- The investigation workflow resumes from its last durable checkpoint.
- Projections can be deleted and rebuilt from the event stream.
- Concurrent and duplicate commands do not violate lifecycle invariants.

## Deferred deliberately

Specialist model reasoning, consensus and production actions are not runtime responsibilities. The runtime exposes durable coordination points for those phases.

