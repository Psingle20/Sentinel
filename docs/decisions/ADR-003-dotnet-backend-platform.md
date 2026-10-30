# ADR-003: .NET backend platform

- Status: Accepted; service names follow the ADR-020 rename (`Sentinel.Api` is `Sentinel.Gateway`)
- Date: 2026-10-27

## Context

The architecture overview and Phase 00 listed the application language as an open decision, but several accepted or planned choices already depend on it:

- Microsoft Orleans is the actor runtime for `Sentinel.Runtime`, and Orleans is a .NET framework.
- The reference deployment maps backend APIs to ASP.NET Core.
- `CleanArchitecture.BuildingBlocks`, the planned foundation package, is a .NET library built on Microsoft dependency injection.

Leaving the language nominally open lets documents and early scaffolding drift while the practical choice has already been made.

## Constraints

- All five backend services share contracts, observability conventions and building blocks; mixing backend languages would duplicate that foundation.
- `Sentinel.Web` is already decided by [ADR-002](ADR-002-frontend-design-system.md).
- The Phase 01 reference system is outside Sentinel and is not bound by this decision.
- Model and provider SDK availability must not dictate the backend language; integrations sit behind Sentinel-owned adapters.

## Considered options

1. .NET for all five backend services.
2. .NET for `Sentinel.Runtime` only, with other languages elsewhere, for example Python for `Sentinel.AgentWorker` or Go for `Sentinel.Ingestion`.
3. A non-.NET backend with an actor runtime other than Orleans.

## Decision

Use C# on .NET for `Sentinel.Api`, `Sentinel.Ingestion`, `Sentinel.Runtime`, `Sentinel.AgentWorker` and `Sentinel.Executor`.

| Item | Pin |
| --- | --- |
| Runtime and SDK | .NET 10 LTS, with the exact SDK pinned in `global.json` |
| Language | The C# version shipped with the pinned SDK |
| Orleans | Orleans 10.x, with the exact version pinned when Phase 00 scaffolds the hosts |
| Dependencies | Central package management through `Directory.Packages.props` |

- ASP.NET Core hosts HTTP boundaries.
- Orleans is used inside `Sentinel.Runtime` only.
- `Sentinel.AgentWorker` reaches the Meta Model API through its `IModelProvider` adapter over HTTP. Working with models does not require Python.
- Python is permitted only for offline evaluation, data preparation and operational scripts under `scripts/`. It is not a production-service language unless a future ADR proves the need.

## Consequences

### Positive

- One toolchain, one contracts implementation and one building-blocks foundation across the backend.
- Orleans, ASP.NET Core and the building blocks compose without interop boundaries.
- Architecture tests, analyzers and package-boundary checks apply uniformly, and contributors can move between services.

### Negative

- Python model-evaluation and data-analysis libraries are not available in-process; offline tooling consumes exported replay data instead.
- Some vendors ship SDKs for other languages first; adapters may need to call HTTP APIs directly.

## Follow-up work

- Record the exact SDK and Orleans versions in `global.json` and `Directory.Packages.props` in Phase 00.
- Decide in the Phase 00 event-semantics ADR whether cross-service and API contracts are schema-first, so `Sentinel.Web` and offline Python tooling can generate types from them.
- Add the backend platform to `PROJECT.json` technologies when the hosts are scaffolded.

## Revisit criteria

Revisit if Orleans is removed from the runtime design, if a backend workload is shown to need a capability .NET cannot reasonably provide, or when .NET 10 approaches the end of its support window. Introducing a second production backend language requires a new ADR and language-neutral contracts first.
