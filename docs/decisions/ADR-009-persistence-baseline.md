# ADR-009: Persistence baseline

- Status: Accepted
- Date: 2026-10-28

## Context

Every service needs durable state, and several decisions already assume PostgreSQL: the production topology permits a shared PostgreSQL server with separate databases, and Temporal uses PostgreSQL ([ADR-005](ADR-005-durable-workflow-engine.md)). The transactional outbox requires the domain write and the outbox record to commit in one transaction ([ADR-008](ADR-008-event-transport.md)).

## Constraints

- Services never share tables, credentials or persistence contexts ([ADR-001](ADR-001-production-first-service-topology.md)).
- `Sentinel.Runtime`'s incident event stream is append-only with optimistic concurrency ([ADR-004](ADR-004-runtime-state-ownership.md)).
- No custom database (architecture §63).

## Considered options

1. PostgreSQL for all service state.
2. PostgreSQL plus a dedicated event-store database for Runtime.
3. A document database for read models and PostgreSQL for writes.

## Decision

Use **PostgreSQL** for all service state.

| Concern | Rule |
| --- | --- |
| Server | One PostgreSQL server locally and in the first deployment; the image is pinned in the Aspire AppHost |
| Databases | One database per service: `api`, `ingestion`, `runtime`, `agentworker`, `executor`. Temporal uses its own database |
| Credentials | One login role per service, with rights only on its own database. Roles are created when a service first creates tables (Phase 01A); Phase 00 provisions databases only |
| Migrations | Owned, versioned and run by each service |
| Data access | EF Core with the Npgsql provider for relational state; direct Npgsql for Runtime's event-stream append and read path |
| Outbox, inbox, idempotency | Tables in each service's own database, created by `CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql`, which joins the caller's connection and transaction under either data-access style |
| Event stream | An append-only table in the `runtime` database with a unique `(stream_id, version)` constraint |
| Observations | Ingestion's observation store starts in PostgreSQL; an analytics store such as ClickHouse is considered only when measured volume requires it (architecture §50) |

## Consequences

### Positive

- One database technology to operate, back up and monitor.
- Domain writes, event appends and outbox records commit atomically within each service.
- Per-service databases and roles keep ownership enforceable, not just conventional.

### Negative

- A shared server is a shared failure and capacity domain until services are split across servers.
- High-volume observation storage may outgrow PostgreSQL and need a second store later.

## Follow-up work

- Add the PostgreSQL server and per-service databases to the Aspire AppHost ([ADR-010](ADR-010-local-orchestration.md)).
- Create per-service roles and migrations when the first tables are introduced in Phase 01A.
- Define backup and restore procedures in Phase 09.

## Revisit criteria

Revisit when observation volume, query latency or tenant isolation requirements exceed what a shared PostgreSQL server provides.
