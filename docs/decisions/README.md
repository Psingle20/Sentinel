# Architecture decisions

Create one Markdown file per consequential decision named `ADR-NNN-short-slug.md`, using the next free number, for example `ADR-008-event-delivery-semantics.md`. Record the decision date inside the file.

Each decision should record status, context, constraints, considered options, decision, consequences, and follow-up work. Start from [TEMPLATE.md](TEMPLATE.md). Supersede old decisions explicitly instead of silently rewriting history.

## Decision index

- [ADR-001: Production-first five-service topology](ADR-001-production-first-service-topology.md) — Accepted
- [ADR-002: Framework-first frontend design system](ADR-002-frontend-design-system.md) — Accepted
- [ADR-003: .NET backend platform](ADR-003-dotnet-backend-platform.md) — Accepted
- [ADR-004: Runtime state ownership](ADR-004-runtime-state-ownership.md) — Accepted
- [ADR-005: Durable workflow engine](ADR-005-durable-workflow-engine.md) — Accepted (Temporal)
- [ADR-006: Remediation control path](ADR-006-remediation-control-path.md) — Accepted
- [ADR-007: Cross-service code and provider access](ADR-007-cross-service-code-and-provider-access.md) — Accepted; amended by ADR-010
- [ADR-008: Event transport](ADR-008-event-transport.md) — Accepted (Redis Streams)
- [ADR-009: Persistence baseline](ADR-009-persistence-baseline.md) — Accepted (PostgreSQL)
- [ADR-010: Local orchestration](ADR-010-local-orchestration.md) — Accepted (.NET Aspire)
- [ADR-011: Reference system location](ADR-011-reference-system-location.md) — Accepted; amended 2026-10-29 with the internal composition rule
- [ADR-012: Event semantics and message envelope](ADR-012-event-semantics.md) — Accepted
- [ADR-013: Building-block package](ADR-013-building-block-reuse.md) — Accepted
- [ADR-014: Provider abstraction](ADR-014-provider-abstraction.md) — Accepted
- [ADR-015: Model abstraction](ADR-015-model-abstraction.md) — Accepted
- [ADR-016: Event time, watermarks and late data](ADR-016-event-time-and-late-data.md) — Accepted
- [ADR-017: Runtime actor and workflow hosting](ADR-017-runtime-actor-and-workflow-hosting.md) — Accepted
- [ADR-018: Observation publication scope](ADR-018-observation-publication-scope.md) — Accepted
- [ADR-019: Incident memory, replay and retention](ADR-019-incident-memory-replay-and-retention.md) — Accepted
- [ADR-020: Application composition and CQRS conventions](ADR-020-application-composition-and-cqrs.md) — Accepted
- [ADR-021: Local AWS for the reference environment](ADR-021-local-aws.md) — Accepted (Floci 2.3.0, pinned by digest)
- [ADR-022: Service-to-service authentication](ADR-022-service-to-service-authentication.md) — Proposed
- [ADR-023: Approver identity](ADR-023-approver-identity.md) — Proposed
- [ADR-024: Model roles beyond the specialists](ADR-024-model-roles.md) — Accepted
