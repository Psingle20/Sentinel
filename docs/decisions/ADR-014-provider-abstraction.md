# ADR-014: Provider abstraction

- Status: Accepted; service names follow the ADR-020 rename (`Sentinel.Api` is `Sentinel.Gateway`)
- Date: 2026-10-28

## Context

Sentinel reads logs, metrics, traces, deployments, topology, alerts and knowledge from external systems, and acts on deployment targets. [ADR-007](ADR-007-cross-service-code-and-provider-access.md) keeps the provider interfaces inside their owning services and makes `Sentinel.Ingestion` the only holder of read credentials. This decision fixes the shape of those interfaces, so that adding a vendor never changes a contract or a domain type.

## Constraints

- Canonical observations are vendor-neutral; nothing downstream of Ingestion sees a vendor format.
- Providers fail, throttle and return partial data. Those conditions must be explicit, not empty results.
- A provider test double must supply observations without any vendor SDK (Phase 00).
- Models never write vendor queries (ADR-007).

## Considered options

1. One generic `IProvider` with a string query and a JSON result.
2. One interface per signal type, returning canonical observations plus a typed outcome.
3. Adopt a third-party abstraction, such as the OpenTelemetry Collector's receivers, as the provider layer.

## Decision

Use option 2.

- `Sentinel.Ingestion.Application` declares one interface per signal type: metrics, logs, traces, deployments and topology. Alerts and knowledge follow the same pattern when their phases add them.
- Each call takes a typed request (tenant, environment, series or stream selector, time range, limit) and returns a `ProviderResult`: canonical observations, a watermark up to which the provider's data is complete, a `Truncated` flag and a `ProviderStatus`. Failures return a typed error, never an exception for an expected outage.
- Adapters live in `Sentinel.Ingestion.Infrastructure`, one per vendor, and translate vendor payloads at the boundary. Vendor names appear only there and in configuration.
- The same adapters serve both scheduled ingestion and federated observation queries, so redaction and error handling are shared.
- Remediation adapters follow ADR-006's `Execute`, `Inspect`, `VerifyAction`, `Compensate` contract inside `Sentinel.Executor`. Notification providers live inside `Sentinel.Api`.
- An in-memory provider double implements every signal interface for tests; a conformance suite run against each adapter checks identity determinism, watermark behaviour, truncation and error mapping.

Option 1 pushes vendor semantics into callers. Option 3 suits ingestion but not federated, typed queries with watermarks, and it would add a deployable.

## Consequences

### Positive

- A new vendor is one adapter class plus configuration, and it must pass the conformance suite.
- Outages, throttling and truncation reach investigators and recovery checks as metadata, not as missing evidence.

### Negative

- Each signal type has its own interface, so cross-cutting changes touch several files.
- Provider-side aggregation is limited to what the typed request can express; richer analysis needs a contract change.

## Revisit criteria

Revisit when a second service needs a provider interface (extract it under ADR-007), or when a signal type cannot be expressed as observations over a time range.
