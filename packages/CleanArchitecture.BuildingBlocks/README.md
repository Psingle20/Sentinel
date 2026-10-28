# Clean Architecture Building Blocks

Generic primitives shared by Sentinel's five services and, later, the distributed job engine. The adoption, layering and extraction plan is in the [building-blocks evolution guide](../../docs/implementation/building-blocks-evolution.md).

## Packages

| Package | Contents | May be referenced by |
| --- | --- | --- |
| `CleanArchitecture.BuildingBlocks.Core` | `Result`, `Error`, `ValidationError`, `Entity`, domain events, clock abstraction | Domain projects and everything above them |
| `CleanArchitecture.BuildingBlocks.Application` | Commands, queries, handlers and dispatchers | Application projects and above |
| `CleanArchitecture.BuildingBlocks.Infrastructure` | Message envelope and registry, outbox, inbox pipeline, dispatcher and outbox health check | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql` | Unit of work, outbox and inbox stores and the messaging schema | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Infrastructure.Redis` | Redis Streams publisher and consumer, with reclaiming and dead-lettering | Service infrastructure projects and hosts |
| `CleanArchitecture.BuildingBlocks.Hosting` | Service registration, validator scanning and handler decorators | Composition roots only |

Namespaces were preserved during the split, so types keep their original `CleanArchitecture.BuildingBlocks.*` namespaces regardless of assembly.

## Provenance

| Item | Value |
| --- | --- |
| Source | `distributed-job-engine` project, `packages/CleanArchitecture.BuildingBlocks` |
| Source revision | `df9da58af89a78e8f4ab2afac02eda162110bf8c` (last change to the package, 2026-09-08); repository head `f77cdeb074b83b357a0a74ba5bcb5f238f87c2d4` |
| Imported | 2026-10-28, by copy without history; the source had no uncommitted changes |
| License | None declared in the source repository. Choose one before Stage F extraction |
| Version | `0.1.0-alpha.3` |

Sentinel builds these packages from this directory and never references the source repository's path.

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

See [CHANGELOG.md](CHANGELOG.md) for changes since the import.
