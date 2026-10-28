# Changelog

## 0.1.0-alpha.3 – 2026-10-28

Stage E of the evolution guide: the infrastructure layer, implementing ADR-012.

### Added

- `Application`: `IOutbox`, `MessageMetadata` and `IUnitOfWork` ports.
- `Infrastructure`: the message envelope, an explicit message-type registry (`AddMessaging`, `Produces`, `Consumes`), the scoped outbox, the outbox dispatcher, the inbound processor (inbox check, handler and commit in one unit of work), a startup gate, an outbox-lag health check and W3C trace propagation through message headers.
- `Infrastructure.PostgreSql`: the unit of work and `IPostgresTransaction`, outbox and inbox stores, and an idempotent schema in a `messaging` schema per service database.
- `Infrastructure.Redis`: the Redis Streams publisher and consumer, with consumer groups, reclaiming of stale pending entries, a maximum delivery count and a `<stream>.dlq` dead-letter stream.
- 33 unit tests and 6 integration tests against PostgreSQL and Redis containers.

### Notes

- The outbox dispatcher retries publish failures with capped exponential backoff and never dead-letters an outbox record: transport failures are transient. The outbox-lag health check reports a growing backlog.
- Not yet included: resilience defaults for non-HTTP dependencies, and retention of published outbox and inbox rows.

## 0.1.0-alpha.2 – 2026-10-28

Stage C of the evolution guide: correct and harden.

### Breaking

- Removed `IDto`, `IProcessor` and `IRequestHandler`. Commands and queries are the only request pipeline.
- `AddBuildingBlocks` now registers every handler and validator in the given assemblies and applies the pipeline itself: validation runs inside logging. `AddLoggingDecorator` and `AddValidationDecorator` are removed.
- Calling `AddBuildingBlocks` twice, or registering two handlers for one command or query, throws `InvalidOperationException`.
- `Entity.DomainEvents` is an `IReadOnlyCollection<IDomainEvent>` snapshot, `Raise` is `protected`, and `ClearDomainEvents` is replaced by `DequeueDomainEvents`, which returns the pending events and clears them.

### Fixed

- The logging decorator uses `ILogger.BeginScope`; the Serilog dependency is removed.
- Validators receive the caller's cancellation token.
- Dispatchers rethrow a handler's original exception instead of a `TargetInvocationException`, and report a missing handler with the request type.
- `Application` no longer depends on `Microsoft.Extensions.DependencyInjection.Abstractions`.

### Added

- Public API baselines (`PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`) in each package.
- Tests for pipeline order, duplicate registration, cancellation, exception preservation, missing handlers, concurrent dispatch and domain events.

## 0.1.0-alpha.1 – 2026-10-28

Stages A and B of the [evolution guide](../../docs/implementation/building-blocks-evolution.md).

- Imported from the distributed job engine at revision `df9da58`.
- Split the single assembly into `Core`, `Application` and `Hosting` without behavior changes. Namespaces are unchanged.
- Dispatcher implementations remain internal to `Application` and are visible to `Hosting` for registration.
- The original nine tests pass unchanged against the split assemblies.
