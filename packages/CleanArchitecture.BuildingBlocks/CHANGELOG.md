# Changelog

## 0.1.0-alpha.8 – 2026-10-29

### Added

- `Infrastructure`: `MessagingOptions.ContextHeaders` maps envelope headers that say whose business a message is, such as a tenant, to names. The inbound processor sets each header the message carries as a tag on the consumer span and opens a log scope with them around the handler, so a handler's logs and traces carry the tenant without the handler adding it. With no headers mapped, nothing changes.
- `Directory.Build.props` carries `alpha.8`.

## 0.1.0-alpha.7 – 2026-10-29

### Added

- `Redis`: `RedisStreamsOptions.MaxConcurrency` handles up to that many entries of one batch at the same time, for consumers whose messages are slow and independent, such as Sentinel's investigations, which each wait on a model. Each entry is still acknowledged, dead-lettered or left pending on its own, and the next poll waits for the whole batch, so a consumer never reclaims an entry it is still handling. The default of 1 keeps the previous in-order behaviour; values outside 1 to 64 fail validation.
- `Directory.Build.props` carries `alpha.7`.

## 0.1.0-alpha.6 – 2026-10-29

### Changed

- **Breaking (prerelease):** `IRequestDispatcher.SendAsync` is now `IRequestDispatcher.HandleRequestAsync`, both overloads, by the owner's decision, so an inbound adapter's one call reads as handing the application a request. Behaviour is unchanged: the processor is still resolved by the request's runtime type. Callers rename the call.
- `Directory.Build.props` carries `alpha.6`.

## 0.1.0-alpha.5 – 2026-10-29

Stage E2 of the evolution guide: the inbound seam, module composition and one type per file, for ADR-020.

### Added

- `Application`: `IRequest`, `IRequest<TResult>`, `IProcessor<TRequest>`, `IProcessor<TRequest, TResult>` and `IRequestDispatcher`. A request is what an HTTP endpoint, message consumer or workflow activity asks the application to do; its processor maps it to exactly one command or query and dispatches it. `IRequestDispatcher.SendAsync` (`HandleRequestAsync` from `alpha.6`) resolves the processor by the request's runtime type through a cached invoker, fails naming the request when none is registered, and surfaces a faulting processor's own exception.
- `Hosting`: `AddBuildingBlocks` registers `IRequestDispatcher` and scans processors with handlers and validators. Each processor is wrapped in request validation, so a request's validators run once, before the processor, and a processor's own code only maps and dispatches. `AddModules` registers `IDependencyModule` instances in the order given, Application, Infrastructure, Api.
- 15 unit tests for request dispatch, the processor pipeline and module composition.

### Changed

- Reverses the `0.1.0-alpha.2` removal of `IDto`, `IProcessor` and `IRequestHandler`. They were removed because they duplicated the command and query pipeline without a defined role; ADR-020 gives the seam one, the only way inbound adapters reach the application layer, and it returns as `IRequest` and `IRequestDispatcher` so that "handler" keeps meaning the use case.
- `AddBuildingBlocks` rejects two processors for one request, as it rejects two handlers for one command or query; the error names both cases.
- Every package file holds one top-level type and is named after it; the nineteen multi-type files are split with namespaces unchanged, so consumers recompile without edits. StyleCop `SA1402` and `SA1649` are errors for the package.
- `Directory.Build.props` carries `alpha.5`; `alpha.4` was recorded here without a version bump.

## 0.1.0-alpha.4 – 2026-10-29

Control-plane configuration documents, for Sentinel's Phase 08 control plane.

### Added

- `Application`: `IVersionedDocumentStore<TDocument>`, `DocumentVersion`, `PublishedDocument<TDocument>` and `AppendOutcome`: an append-only, linear history of one configuration document.
- `Infrastructure.PostgreSql`: `PostgresVersionedDocumentStore<TDocument>`, one table per document; a transaction-scoped advisory lock makes the based-on-newest check and the insert atomic, so concurrent publishes from one version produce exactly one winner.
- `Infrastructure`: `AddManagedDocument<TOptions, TDocument>`, which applies the current published document over a service's bound options, recomputes them when a new version appears, refreshes on an interval, and makes `IOptions<TOptions>` return the current value.
- 5 unit tests and 6 integration tests against PostgreSQL.

### Notes

- A failed refresh keeps the version already in effect; before anything is published the deployment configuration applies unchanged.
- Consumers that copy `IOptions<TOptions>.Value` into a field at construction keep the old value; read `Value` per operation to see a published change.

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
