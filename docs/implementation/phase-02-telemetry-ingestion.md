# Phase 02 – Telemetry and canonical ingestion

## Outcome

Convert logs, metrics, traces, deployment changes, topology and alerts from the reference system into durable, tenant-scoped canonical observations suitable for correlation and replay.

## Primary service ownership

`Sentinel.Ingestion` owns provider access, normalization, checkpoints and publication. `Sentinel.Runtime` consumes canonical observations; `Sentinel.Gateway` exposes provider configuration. The phase exits only when an observation crosses those boundaries into the golden incident path.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) single pull adapter and two observation types with the full ingestion pipeline. The skeleton's observation query contract is extended, not replaced. The end-to-end suite must pass with the new pipeline on the path.

## In scope

- One complete reference integration for each required signal type.
- Canonical `Observation`, `LogObservation`, `MetricObservation`, `TraceObservation`, `DeploymentObservation` and `TopologyObservation` schemas.
- Provider checkpoints, authentication references, rate-limit handling and normalized errors.
- Event-time and ingest-time handling, watermarks, allowed lateness and out-of-order processing.
- Enrichment with tenant, environment, service, ownership, topology, trace context and schema version.
- Deterministic observation identity and duplicate suppression.
- Durable event transport abstraction, hardened from the implementation selected in Phase 00 and used by the walking skeleton.
- Transactional outbox where state and messages must change atomically.
- Inbox/consumer deduplication for retry-safe processing.
- Dead-letter handling and operator-visible replay controls.
- Stream prioritization so active incident and SLO signals outrank background data.
- Provider health, latency, error and quota telemetry.
- Read-only observation query contract for `Sentinel.AgentWorker` tools and `Sentinel.Runtime` recovery checks, served from the observation store or by federated provider query with ingestion redaction applied ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)).
- Query response metadata: `ObservedThrough`, `Freshness`, `Completeness`, `Truncated`, `ProviderHealth`, `RedactionPolicyVersion` and `ContinuationToken`, derived from the event-time watermarks and provider health tracked above.
- Provider interfaces owned inside `Sentinel.Ingestion`, not in a shared package (ADR-007).
- Bulkhead between streaming ingestion and query serving, so neither can starve the other during an incident.

## Data flow

```text
Provider -> Adapter -> Validate -> Normalize -> Enrich
         -> Outbox/Event transport -> Inbox -> Observation store
```

Raw provider payloads may be retained under a governed retention policy, but downstream domain logic consumes only Sentinel-owned contracts.

## Primary deliverables

1. Provider SDK abstraction and reference adapters for all golden-scenario signals.
2. Versioned canonical observation schemas and compatibility tests.
3. Ingestion workers with checkpoints, retries, backpressure and DLQ handling.
4. Observation persistence and the ADR-007 query contract by tenant, service, type and time range, including bounded provider-side aggregation.
5. Event-time policy and late-event behavior documented as an ADR.
6. Operational dashboard for throughput, lag, duplicates, failures and provider health.

## Reliability semantics

- Delivery target: at least once.
- Processing requirement: idempotent.
- Duplicate key: deterministic from tenant, provider, source identity and source version/time.
- Poison data: quarantined with reason and safe replay support.
- Provider outage: bounded retry with circuit breaker and visible degraded state.
- Load spike: bounded queues, priority and backpressure rather than uncontrolled memory growth.

## Security and privacy

- Validate payload size and schema before processing.
- Treat log text, trace attributes and documents as untrusted data.
- Redact or tokenize configured sensitive attributes before persistence or model access.
- Resolve credentials through secret references; never copy provider secrets into domain events.
- Enforce tenant isolation on ingestion, storage and queries.

## Test plan

- Contract and adapter conformance tests.
- Duplicate, late, missing, malformed and out-of-order event tests.
- Provider timeout, throttling and partial-response tests.
- Outbox/inbox crash-recovery tests.
- Backpressure and DLQ replay tests.
- Query metadata tests: late data keeps `ObservedThrough` behind the requested window; a failing provider sets `Completeness` and `ProviderHealth`; limits set `Truncated` and return a working `ContinuationToken`.
- Bulkhead tests: an investigation query burst does not stall ingestion, and an ingestion spike does not starve queries.
- Golden scenario assertions for every expected signal type.

## Exit criteria

- The golden scenario produces queryable canonical observations for every required source.
- Reprocessing the same source data does not create logical duplicates.
- Late and out-of-order events follow the documented event-time policy.
- A provider outage cannot cascade into unbounded resource consumption.
- Observation provenance reaches the original provider resource and timestamp.
- Query results returned to AgentWorker are redacted exactly as ingested data is, and provider outages surface as explicit query errors.
- Every query response reports accurate freshness, completeness and truncation metadata.

## Deferred deliberately

Additional vendors are not added for breadth. The adapter interfaces and conformance suite make them possible after the complete product loop works.

