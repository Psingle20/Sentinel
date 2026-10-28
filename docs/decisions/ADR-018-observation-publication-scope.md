# ADR-018: Observation publication scope

- Status: Accepted
- Date: 2026-10-28

## Context

Phase 02 added logs, traces and topology to Ingestion. Until then every new observation was published as `ObservationRecorded` on `sentinel.observations`. With logs and spans on the stream, the messaging Redis held hundreds of megabytes within one end-to-end run. With `appendfsync always` ([ADR-008](ADR-008-event-transport.md)), AOF rewrites then blocked it for minutes: Executor waited 570 s on one `XAUTOCLAIM`, and detection stalled behind it. No service consumes log or trace events; AgentWorker reads them through the query contract ([ADR-007](ADR-007-cross-service-code-and-provider-access.md)).

## Constraints

- The event transport stays durable: `appendfsync always` is kept.
- Every observation is still stored, deduplicated and queryable with the same metadata.
- Runtime must still receive what drives correlation: metrics for detection, deployments for changes, topology for dependency edges.

## Considered options

1. Publish every observation and trim streams harder.
2. Publish only kinds with an event consumer, configurable as `Ingestion:PublishKinds`, and serve the rest through the query contract.
3. Move high-volume kinds to a separate transport.

## Decision

Adopt option 2.

- Ingestion publishes `Metric`, `Deployment` and `Topology` observations by default. Logs and traces are stored and served by `/v1/observations/query`.
- A new consumer of another kind adds that kind to `Ingestion:PublishKinds` in the same change that adds the consumer.
- Ingestion's own streams trim at about 20,000 entries (`MaxStreamLength`), about half an hour of correlation signals at reference load.

## Consequences

### Positive

- The messaging Redis stays small and fast, so fsync-per-write durability stays affordable.
- Bulk telemetry travels through the bounded, bulkheaded query path built for it.

### Negative

- A future service that wants a log or trace event feed must opt in deliberately.

## Follow-up work

- Phase 09: load tests confirm the stream sizes and the Redis memory budget.

## Revisit criteria

Revisit if a consumer needs streaming logs or traces with lower latency than the query contract gives.
