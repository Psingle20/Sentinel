# ADR-013: Building-block package

- Status: Accepted
- Date: 2026-10-28

## Context

Sentinel's five services need the same primitives: `Result` and typed errors, an entity with domain events, commands, queries and their dispatchers, validation and logging decorators, and the messaging mechanics every service would otherwise repeat, such as the outbox, the inbox, idempotency and the Redis Streams transport. Writing them five times would make the services drift, and [ADR-007](ADR-007-cross-service-code-and-provider-access.md) restricts shared code to a short list of packages, so where these primitives live has to be decided before the services are built. Stage A of the [building-blocks evolution guide](../implementation/building-blocks-evolution.md) requires that decision to be recorded.

## Constraints

- The building blocks contain no Sentinel concepts: no tenant, incident, evidence, policy or provider type.
- Each service's domain project may reference only the dependency-free Core package ([ADR-007](ADR-007-cross-service-code-and-provider-access.md)); application and domain projects never receive an infrastructure package, directly or transitively.
- The package must not become a message broker, workflow engine, event store or Orleans abstraction; it adapts PostgreSQL and Redis Streams, which Sentinel already runs.

## Considered options

1. Write the primitives inside each service, with no shared package.
2. Adopt a third-party mediator and messaging library and organise the services around its conventions.
3. Write a Sentinel-owned, generic `CleanArchitecture.BuildingBlocks` package, layered from the start and versioned, and extract it into its own repository once it has proven its boundaries.

## Decision

Use option 3.

- The package lives at `packages/CleanArchitecture.BuildingBlocks` and is written for Sentinel, with a changelog from its first prerelease.
- It is split into `Core`, `Application`, `Infrastructure` (plus one package per technology, `Infrastructure.PostgreSql` and `Infrastructure.Redis`) and `Hosting`, with each service layer referencing only the matching depth.
- Sentinel owns the package during `0.x`. Every change is recorded in the package changelog, and public APIs are fixed by API baselines so that a change to the surface is visible in review.
- Extraction into a standalone, versioned repository happens only when the guide's Stage F criteria hold, including a second product consuming the same packages. Until then the package is built from the Sentinel repository and not published.

Option 1 would repeat the same mechanics in five places and let them diverge. Option 2 would organise Sentinel's use cases around a library's conventions, add a dependency to every domain project's neighbours, and still leave the outbox, inbox and transport to be written.

## Consequences

### Positive

- The primitives are written once, tested once and used by all five services, the deploy controller and the scenario runner.
- Layer boundaries are enforced by project references, analyzers, API baselines and the services' architecture tests.
- The package can stand alone later, because nothing in it knows about Sentinel.

### Negative

- Sentinel carries a generic package that is not its product; its tests, baselines and changelog are maintenance the services pay for.
- No license is declared yet. One must be chosen before extraction.

## Revisit criteria

Revisit when a second product wants the packages, when the surface has been stable through two feature cycles and extraction becomes worthwhile, or if maintaining the package starts costing more than the consistency it buys.
