# ADR-013: Building-block reuse

- Status: Accepted
- Date: 2026-10-28

## Context

The distributed job engine already contains `CleanArchitecture.BuildingBlocks`: `Result`, errors, a domain-event entity, commands, queries and dispatchers, with tests and production use. Sentinel's five services need the same primitives, plus messaging mechanics that every service would otherwise repeat. Stage A of the [building-blocks evolution guide](../implementation/building-blocks-evolution.md) requires a recorded decision before adoption.

## Constraints

- Sentinel must not depend on a machine-specific path to the job engine's repository.
- The building blocks contain no Sentinel or job-engine concepts.
- Each service's domain project may reference only the dependency-free Core package ([ADR-007](ADR-007-cross-service-code-and-provider-access.md)).

## Considered options

1. Reference the job engine's source in place.
2. Copy the package into Sentinel, split it into layers, and evolve it there until both consumers have proven its boundaries.
3. Write Sentinel's own primitives from scratch.

## Decision

Use option 2.

- The package is copied into `packages/CleanArchitecture.BuildingBlocks` with a provenance record: source revision, import date and license status.
- It is split into `Core`, `Application`, `Infrastructure` (plus one package per technology) and `Hosting`, with each service layer referencing only the matching depth.
- Sentinel owns the copy during `0.x`. Every change is recorded in the package changelog, and public APIs are fixed by API baselines.
- The job engine keeps its own copy until it adopts the split packages. Extraction into a standalone, versioned repository happens only when the guide's Stage F criteria hold, including both products consuming the same packages.

## Consequences

### Positive

- Proven primitives are reused rather than rewritten.
- Layer boundaries are enforced by project references, analyzers and API baselines.

### Negative

- Two copies diverge until the job engine adopts the split packages; fixes must be ported deliberately.
- No license is declared yet. One must be chosen before extraction.

## Revisit criteria

Revisit when the job engine is ready to adopt the split packages, or if divergence between the two copies starts costing more than extraction would.
