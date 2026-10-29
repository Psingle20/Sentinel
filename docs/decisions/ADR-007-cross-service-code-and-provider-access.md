# ADR-007: Cross-service code and provider access

- Status: Accepted; amended by [ADR-010](ADR-010-local-orchestration.md) on 2026-10-28; service names follow the ADR-020 rename (`Sentinel.Api` is `Sentinel.Gateway`)
- Date: 2026-10-27

## Context

The planned `packages/` list included `Sentinel.Domain`. If all five services reference one domain package, any change to an aggregate or invariant forces every service to rebuild and redeploy, and services can start relying on each other's internal rules. That is the distributed monolith [ADR-001](ADR-001-production-first-service-topology.md) is meant to prevent.

The same list included `Sentinel.Providers.Abstractions` and `Sentinel.Models.Abstractions`. Each interface family in them has exactly one consuming service, so sharing them adds coupling without a second consumer.

Separately, no document stated how `Sentinel.AgentWorker` read tools reach telemetry. AgentWorker may not hold production credentials, `Sentinel.Ingestion` owns provider connections, and redaction happens in Ingestion before model access. Runtime recovery verification also needs targeted observation queries.

## Constraints

- Services share no tables, credentials or persistence contexts.
- Packages contain only stable reusable code with at least one named consumer outside its origin. They are not created for architectural symmetry.
- Telemetry, documents and runbooks are untrusted, and sensitive attributes are redacted before model access (Phase 02).
- Sentinel does not build a telemetry storage backend (architecture §63).

## Considered options

Shared code:

1. One shared `Sentinel.Domain` package used by every service.
2. Each service owns its domain module; only contracts are shared.

Provider read access:

1. AgentWorker holds read-only provider credentials and queries telemetry backends directly.
2. Ingestion stores all telemetry and serves every query from its own store.
3. Ingestion exposes a read-only query contract and fulfills it from its observation store or by federated query through its provider adapters.

## Decision

### Shared code

Use option 2. `Sentinel.Domain` is removed from `packages/`.

| Service | Owns the domain for |
| --- | --- |
| `Sentinel.Runtime` | Incidents, investigations, evidence acceptance, hypotheses, decisions, plan lifecycle and recovery |
| `Sentinel.Executor` | Policy, capabilities, execution, compensation and action verification |
| `Sentinel.Gateway` | Control-plane configuration: tenants, environments, catalog, SLO definitions, approvals and identity |
| `Sentinel.Ingestion` | Provider connections, checkpoints, normalization and observation queries |
| `Sentinel.AgentWorker` | Agent executions, tool calls and model metadata |

Services share data contracts, not aggregates or business rules. The shared packages are:

| Package | Contents | Consumers |
| --- | --- | --- |
| `CleanArchitecture.BuildingBlocks` (Core, Application, Hosting) | Generic primitives with no Sentinel concepts | All five services |
| `Sentinel.Contracts.Primitives` | Strongly typed identifiers and schema-version types, with no dependencies | All five services, including their domain modules |
| `Sentinel.Contracts` | Canonical observations, integration events, cross-service commands, and finding, plan, attestation and execution contracts | All five services' application and infrastructure layers |
| `Sentinel.ServiceDefaults` | OpenTelemetry setup and conventions, health endpoints, service discovery and HTTP resilience. Replaced the planned `Sentinel.Observability` package under [ADR-010](ADR-010-local-orchestration.md) | All five services |

Contracts are schema-versioned data shapes with shape validation only. A service's domain module may reference `Sentinel.Contracts.Primitives` but not `Sentinel.Contracts`; its application layer maps between contracts and domain types.

Provider and model interfaces stay inside their owning services until multiple concrete consumers exist:

| Interfaces | Owner |
| --- | --- |
| Log, metric, trace, topology, alert, deployment and knowledge providers | `Sentinel.Ingestion` |
| Remediation providers (adapters) | `Sentinel.Executor` |
| Notification providers | `Sentinel.Gateway` |
| `IModelProvider` | `Sentinel.AgentWorker` |

### Provider read access

Use option 3.

- `Sentinel.Ingestion` is the only service that holds credentials for observability, deployment, topology and knowledge providers.
- Ingestion exposes a read-only, tenant-scoped observation query contract by service, signal type and time range. Bounded provider-side aggregation is allowed, such as grouped error signatures or metric range queries.
- Queries are answered from the observation store when the data was ingested, or by federated query through the provider adapter. Results are normalized to canonical observations and pass through the same redaction as ingestion.
- Queries are typed. Models cannot generate arbitrary vendor queries.
- `Sentinel.AgentWorker` read tools and `Sentinel.Runtime` recovery verification use this contract. AgentWorker holds no provider credentials.
- AgentWorker records tool-call inputs and the canonical results it received, including the response metadata below, in its execution metadata. This supports investigation replay (Phase 07, level 2).
- Remediation adapters and their privileged credentials remain in `Sentinel.Executor`. Notification adapters remain in `Sentinel.Gateway`.

Every query response carries metadata alongside its values:

| Field | Meaning |
| --- | --- |
| `ObservedThrough` | Event time up to which the result is complete (the watermark) |
| `Freshness` | Age of the newest data relative to the query time |
| `Completeness` | Whether every requested source and series answered |
| `Truncated` | Whether limits cut the result short |
| `ProviderHealth` | Health of the providers consulted |
| `RedactionPolicyVersion` | Redaction rules applied to the result |
| `ContinuationToken` | Cursor for the remainder of a truncated result |

Recovery verification is `RecoveryInconclusive`, never `RecoveryVerified`, unless `ObservedThrough` covers the plan's full observation window and the result is complete and untruncated. The same metadata tells investigators when evidence is partial.

## Consequences

### Positive

- A domain change in one service does not force the others to redeploy.
- Credentials, redaction, rate limits and provider error handling live in one place, and adapters are reused for ingestion and queries.
- Model context never receives telemetry that skipped redaction.
- Runtime cannot declare recovery from stale or incomplete telemetry.

### Negative

- Some concepts are expressed twice: as a contract shape and as a richer type inside the owning service.
- Ingestion is on the investigation critical path. Query serving must be bulkheaded from streaming ingestion so investigation queries during an outage cannot starve ingestion, and vice versa.
- Federated queries depend on provider availability; outages surface as explicit tool errors or incomplete results, never as empty evidence.

## Follow-up work

- Define the observation query contract and response metadata in Phase 02, and use their minimal form in the Phase 01A walking skeleton.
- Add architecture tests that reject a reference from one service to another service's domain module, and from a domain module to `Sentinel.Contracts`.
- Extract a provider or model interface into a package only when a second concrete consumer exists, recorded as an amendment to this ADR.

## Revisit criteria

Revisit if a genuinely shared, stable domain rule emerges with at least two consumers and no service-specific behavior, or if query load on Ingestion justifies a separate query deployable under a new ADR.
