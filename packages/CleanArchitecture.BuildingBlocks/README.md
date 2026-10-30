# Clean Architecture Building Blocks

Generic primitives written for Sentinel's five services and kept free of Sentinel concepts, so that the packages can stand alone once they have a second consumer. The layering, hardening and extraction plan is in the [building-blocks evolution guide](../../docs/implementation/building-blocks-evolution.md), and the decision is [ADR-013](../../docs/decisions/ADR-013-building-block-reuse.md).

## Packages

| Package | Contents | May be referenced by |
| --- | --- | --- |
| `CleanArchitecture.BuildingBlocks.Core` | `Result`, `Error`, `ValidationError`, `Entity`, domain events, clock abstraction | Domain projects and everything above them |
| `CleanArchitecture.BuildingBlocks.Application` | Commands, queries, handlers and dispatchers | Application projects and above |
| `CleanArchitecture.BuildingBlocks.Infrastructure` | Message envelope and registry, outbox, inbox pipeline, dispatcher and outbox health check | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql` | Unit of work, outbox and inbox stores and the messaging schema | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Infrastructure.Redis` | Redis Streams publisher and consumer, with reclaiming and dead-lettering | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Hosting` | Service registration, validator scanning and handler decorators | Composition roots only |

Namespaces follow a type's role, not its assembly: every type lives under `CleanArchitecture.BuildingBlocks.*`, so a type can move between assemblies without consumer edits.

## Versioning

| Item | Value |
| --- | --- |
| Version | `0.1.0-alpha.8`, carried by `Directory.Build.props` |
| Owner | Sentinel, during `0.x` ([ADR-013](../../docs/decisions/ADR-013-building-block-reuse.md)) |
| License | None declared yet. Choose one before Stage F extraction |
| Publication | Built from this directory by project reference; not published to a feed until extraction |

## Messaging

A service declares its messages explicitly and chooses its storage and transport:

```csharp
builder.Services.AddMessaging("sentinel-runtime")
    .Produces<IncidentOpened>("runtime.incident.opened", 1, "sentinel.incidents")
    .Consumes<ObservationRecorded, CorrelateObservation>("ingestion.observation.recorded", 1, "sentinel.observations")
    .AddPostgresStorage()
    .AddRedisStreams();
```

`AddPostgresStorage` needs an `NpgsqlDataSource` for the service's own database, and `AddRedisStreams` needs an `IConnectionMultiplexer` for the messaging Redis. Application code enqueues through `IOutbox` and commits through `IUnitOfWork`. Repositories join the same transaction through `IPostgresTransaction`, so domain writes, inbox records and outbox messages commit together.

The integration tests start PostgreSQL and Redis containers, so Docker must be running:

```powershell
dotnet test packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests
```

## Changing the public API

Each package declares its public surface in `PublicAPI.Unshipped.txt`. Adding, changing or removing a public member fails the build until that file is updated, so API changes are always visible in review. Record every change in the changelog.

## Registration

Call `AddBuildingBlocks` once from the composition root with the assemblies that contain handlers and validators. It registers the dispatchers, every command and query handler and every validator, and wraps handlers with validation inside logging.

## Commands

Run from the Sentinel project root:

```powershell
dotnet test packages/CleanArchitecture.BuildingBlocks/tests/CleanArchitecture.BuildingBlocks.Tests
```

See [CHANGELOG.md](CHANGELOG.md) for the history of every version.
