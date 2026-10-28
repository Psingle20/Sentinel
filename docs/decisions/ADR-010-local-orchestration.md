# ADR-010: Local orchestration

- Status: Accepted
- Date: 2026-10-28

## Context

Developers must run five backend services, the web frontend, PostgreSQL, the messaging Redis and, later, Temporal and the reference system together. Phase 00 also requires an observability baseline and health and readiness endpoints for every service.

## Constraints

- The backend is .NET ([ADR-003](ADR-003-dotnet-backend-platform.md)).
- Containers run locally through Docker.
- Local orchestration must not become the production deployment definition by accident.

## Considered options

1. .NET Aspire AppHost.
2. Docker Compose.

## Decision

Use **.NET Aspire** for local orchestration.

- The AppHost lives at `deploy/local/Sentinel.AppHost`. It starts the five services, PostgreSQL with one database per service ([ADR-009](ADR-009-persistence-baseline.md)) and the dedicated messaging Redis ([ADR-008](ADR-008-event-transport.md)).
- Later phases add the web frontend, Temporal (Phase 03) and the reference system with its own Redis and observability stack (Phase 01, [ADR-011](ADR-011-reference-system-location.md)).
- Every host references `Sentinel.ServiceDefaults`, which configures OpenTelemetry traces, metrics and logs with OTLP export, service discovery, HTTP client resilience and the `/health/live` and `/health/ready` endpoints.
- The Aspire dashboard is the local telemetry view.
- Aspire is local orchestration only. Deployment definitions remain under `deploy/` and are decided separately.

This amends [ADR-007](ADR-007-cross-service-code-and-provider-access.md): `Sentinel.ServiceDefaults` replaces the planned `Sentinel.Observability` package.

## Consequences

### Positive

- One command starts the whole system, with service discovery and connection strings wired automatically.
- Telemetry is visible locally from the first commit.
- Orchestration is C#, so it is type-checked and refactored with the services.

### Negative

- Docker must be running for any local end-to-end run, with CLI and engine 25.0 or newer: Aspire 13.6's orchestrator rejects older Docker versions as unhealthy and starts no containers.
- Orchestration knowledge lives in C#, which is less familiar to contributors outside .NET.

## Follow-up work

- Add the web frontend to the AppHost when `Sentinel.Web` is scaffolded.
- Add Temporal and its database in Phase 03.

## Revisit criteria

Revisit if Aspire limits running the reference system or the web frontend, or if deployment tooling makes a shared Compose definition more valuable.
