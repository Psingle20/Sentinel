# ADR-005: Durable workflow engine

- Status: Accepted
- Date: 2026-10-27

## Context

Architecture §12 requires long-running orchestration with retries, timeouts, cancellation, compensation, waiting for human input, crash recovery, idempotent activities, durable timers and child workflows. The investigation and remediation flows need all of them: approval waits, recovery observation windows, investigator fan-out and partial-remediation compensation.

The engine choice shapes the `Sentinel.Runtime` design, local orchestration and the production deployment. [ADR-004](ADR-004-runtime-state-ownership.md) limits the engine to step progress, so domain state does not depend on it.

## Constraints

- Sentinel does not build custom infrastructure such as databases, brokers or consensus protocols (architecture §63).
- The reference deployment stays vendor-neutral and cloud-neutral (architecture §50).
- Workflow workers run inside `Sentinel.Runtime`; the engine must not require a new Sentinel deployable ([ADR-001](ADR-001-production-first-service-topology.md)).
- The backend is .NET ([ADR-003](ADR-003-dotnet-backend-platform.md)).
- The engine holds progress only; the incident event stream remains authoritative.

## Considered options

1. **Temporal**, self-hosted, with the .NET SDK and PostgreSQL persistence.
2. **Dapr Workflow**, which requires the Dapr sidecar runtime beside each workflow host.
3. **Durable Task Framework** with a self-hosted storage provider, or Azure's managed Durable Task Scheduler.
4. **Orleans-native orchestration**: workflow progress as an event-sourced grain with Orleans reminders for timers.

## Decision

Use **Temporal**.

- Temporal natively provides every semantic §12 lists: durable approval waits through signals, timers for observation windows, retries and timeouts, cancellation, compensation, crash recovery, workflow versioning, long-running execution, fan-out and operational visibility.
- The Temporal server and .NET SDK are open source under the MIT license.
- It is self-hostable and cloud-neutral, and its server can use the PostgreSQL instance Sentinel already plans to run, in its own database.
- The Temporal server is infrastructure, like PostgreSQL or the event transport. It is not a Sentinel service, and no Sentinel service reads its database directly.

Orleans and Temporal solve different problems and are used together:

```text
Orleans   entity ownership, concurrency and domain command serialization
Temporal  long-running orchestration, waits, retries and compensation
```

Options 2 and 3 are not preferred: Dapr adds a sidecar runtime for a single consumer, and the managed Durable Task option is cloud-bound. Option 4 would make Sentinel responsible for the correctness of durable timers, retry policy, signals, cancellation and versioning, which conflicts with the no-custom-infrastructure principle.

No pre-acceptance spike is required. ADR-004 keeps the engine replaceable, and Temporal is validated through the Phase 03 acceptance tests below.

The [Phase 01A walking skeleton](../implementation/phase-01a-walking-skeleton.md) does not use a workflow engine; this decision is first exercised in Phase 03.

## Implementation constraints

- **Temporal is never the source of truth for incidents.** Workflow history records orchestration progress; Runtime's event stream records domain truth.
- **Workflow code performs no I/O.** It never calls Orleans, databases, other services or the clock directly. It schedules activities, and activities query or command `IncidentGrain`:

  ```text
  Temporal workflow
    -> Runtime activity
        -> IncidentGrain
            -> event stream
  ```

- **Activities are idempotent**, keyed by incident ID, workflow run ID and step (ADR-004).
- **Payloads are references.** Activities pass IDs and small summaries, never raw telemetry, tool output or model responses. Temporal limits payload and history size, and that data belongs in Runtime's event stream or AgentWorker's execution records.
- **Fan-out uses parallel activities first.** A child workflow is used only when an investigator needs an independent lifecycle, a separate history or separate worker ownership, not automatically for every agent.
- **Versioning is explicit.** Changes to workflow code that affect in-flight runs use Temporal's versioning mechanism, with a test for each change.

## Phase 03 acceptance tests

- Workflow restart: a Runtime worker killed mid-investigation resumes from the last completed activity.
- Approval signal: approval, rejection, expiry and duplicate signals behave correctly.
- Duplicate activity execution does not append duplicate events.
- Runtime and Temporal server failures, independently and together, lose no accepted state.
- Workflow upgrade: an in-flight run survives a versioned code change.
- PostgreSQL backup and restore of the Temporal database, followed by resumption of in-flight workflows.

## Consequences

### Positive

- Approval waits, recovery windows, fan-out and compensation use tested engine primitives instead of Sentinel-built ones.
- The Temporal UI and history make stuck or failed orchestration diagnosable.
- Workflow versioning gives a defined path for changing in-flight investigations.

### Negative

- One more stateful component in local orchestration, deployment, backup and upgrades.
- Determinism constraints need analyzers or review discipline.
- Workflow-version changes need care while investigations are in flight.

## Follow-up work

- Add the Temporal server and its database to local orchestration and the deployment definition.
- Record Orleans and Temporal hosting together in the Orleans hosting ADR.

## Revisit criteria

Revisit if operating the Temporal server costs more than the semantics it provides, if determinism constraints materially slow delivery, if its license changes, or if a lighter engine meets §12 with less operational load.
