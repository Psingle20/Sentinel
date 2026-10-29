# ADR-020: Application composition and CQRS conventions

- Status: Proposed
- Date: 2026-10-29, revised the same day after review against both reference projects

## Context

Sentinel's five services already separate Domain, Application, Infrastructure and Host projects (renamed Api below), and its shared building blocks already provide commands, queries, dispatchers, validators and decorators. The current service implementations do not consistently use those boundaries: hosts initiate building-block registration, infrastructure extensions register application services, application ports are collected into broad `Ports.cs` files, interfaces, records and classes are clubbed into files by convenience, message consumers call application services directly from Infrastructure, and use cases are exposed through procedural services called inline from endpoints.

Two reference projects demonstrate the patterns Sentinel lacks. The clean-architecture template (`cleanarchitecure`) composes one `IDependencyModule` per layer through a `DependencyContainer` in a fixed order, places a processor between transport and the command or query dispatcher, and maps `Result` to HTTP in one place. The distributed job engine organises commands and queries as feature folders per operation, keeps one interface per abstraction file, catalogues domain errors per aggregate and enforces layering with architecture tests. The job engine's empty Domain module is the one pattern not adopted, because a Domain DI module would violate Sentinel's stricter dependency rules.

Without a project-wide convention, each service will refactor toward a different structure and the existing CQRS package will remain nominal rather than architectural.

## Constraints

- Preserve the five-service topology and service ownership from ADR-001.
- Preserve service autonomy and the allowed shared packages from ADR-007.
- Reuse and evolve the split building blocks according to ADR-013; do not introduce another mediator package.
- Keep Domain independent of `Sentinel.Contracts`, hosting, DI and technology SDKs.
- Preserve Orleans single-writer ownership and Temporal determinism from ADR-004, ADR-005 and ADR-017.
- Preserve public APIs, versioned integration contracts, idempotency and the complete incident path while refactoring.

## Considered options

1. Keep the current registration and procedural application-service structure, making only local file splits.
2. Copy the distributed job engine's `IDependencyModule` pattern into every layer, including Domain.
3. Adopt feature-oriented CQRS with layer-owned dependency modules for Application, Infrastructure and Api, a processor seam for every inbound request, and one type per file, adapted to Sentinel with a DI-free Domain.
4. Replace the existing building blocks with MediatR and reorganize around its conventions.

## Decision

Choose option 3, subject to implementation and validation of the first vertical refactoring slice.

- **Layer-owned modules.** Each Application, Infrastructure and Api project owns one `IDependencyModule` implementation, using the interface already in `CleanArchitecture.BuildingBlocks.Hosting`. A `DependencyContainer` in each Api project composes them in the order Application, Infrastructure, Api. The Application module calls `AddBuildingBlocks` for its own assembly and nothing else registers handlers; the Infrastructure module binds options and implements ports and registers nothing from Application; the Api module registers transport, identity and endpoint concerns only. Domain has no module and no dependency-injection reference.
- **One inbound path.** Every HTTP endpoint, integration-message consumer and Temporal activity constructs an application `<Operation>Request` and sends it through `IRequestDispatcher`. A `<Operation>Processor` validates the request, maps it to exactly one command or query and dispatches it to the handler that owns the use case. `IRequest<T>`, `IProcessor<,>` and `IRequestDispatcher` are reinstated in `CleanArchitecture.BuildingBlocks.Application` with this defined role, reversing their removal in building-blocks `0.1.0-alpha.2`, and `AddBuildingBlocks` scans processors with handlers and validators.
- **Application layout.** By the owner's decision on 2026-10-29, an Application project separates what a caller asks from the work that carries it out, each split by capability: `BusinessProcesses/<Capability>/` for requests, their processors and what they return; `BusinessTasks/Commands/<Capability>/` and `BusinessTasks/Queries/<Capability>/` for the commands and queries a processor dispatches, with their handlers; `Validators/<Capability>/` for request validators. This replaces a feature folder per operation. Application ports live one per file under `Abstractions/<Kind>/`, options under `Options/`. Error catalogues are per aggregate, in Domain. Inbound adapters reach a processor with `IRequestDispatcher.HandleRequestAsync`, which resolves it by the request's type.
- **One type per file**, named for the type, enforced by StyleCop rules `SA1402` and `SA1649`, introduced at warning severity with a suppression list and raised to error when the migration completes. Closed polymorphic hierarchies keep the base in one file and each derived type in its own. Enums are types and get their own files.
- **Rich domain, orchestrating application, mapping infrastructure.** Every rule that decides an outcome lives in a Domain aggregate or domain policy, returns `Result` with an error from that aggregate's catalogue, and, where there is a lifecycle, checks an explicit transition table. Aggregates are sealed with private state and behaviour methods; handlers load, call one method, persist and publish; Infrastructure maps rows, documents and events to domain types and evaluates nothing. `Incident` stays the single root and stream (ADR-004) and gains child entities per lifecycle; Api gains an `Approval` aggregate; recovery criteria move from Application into Domain. The guide's "Domain modelling" section lists the rules and the per-service changes.
- **Service and layer naming.** Service directories are `services/Sentinel.<Name>/`, named like their projects (the owner decided on 2026-10-29 against a `Service` folder suffix); the projects inside keep the short name, `Sentinel.<Name>.Api`, `.Application`, `.Domain` and `.Infrastructure`. The outer layer is `Api`, not `Host`, so projects sort in dependency order and match both reference projects. `Sentinel.Api` becomes `Sentinel.Gateway` in `services/Sentinel.Gateway/`, because `Sentinel.Api.Api` is not usable; the other four keep their short names. Resource and producer names are `gateway-service`, `runtime-service` and so on. Message type and stream names, Orleans cluster and service IDs, Temporal identities, database names and attestation issuer and audience are contracts or durable identities and do not change with the rename. The guide's "Service and layer naming" section carries the full mapping.
- **Thin adapters.** `Program.cs` is composition and middleware. HTTP endpoints are MVC controllers, one per capability under Api `Controllers/`, deriving from a per-service `ApiController` base that maps `Result` to HTTP, as both reference projects do; Minimal APIs are retired from the hosts so the template, the job engine and Sentinel share one HTTP convention. Routes, status codes and bodies do not change in the move. Message registration and consumers move to Infrastructure `Messaging/`; a consumer maps a contract to a request and sends it.
- Shared integration contracts do not implement `IRequest`, `ICommand` or `IQuery`. Consumers map them.
- Stable protocol, configuration, service, route-root and telemetry identifiers use owner-scoped constants. Sentinel will not create a global constants class.
- Orleans grains and Temporal workflows retain their responsibilities. Handlers reach the incident only through `IIncidentOwner`; `IncidentCommand` remains the aggregate-level command set and is distinct from application `ICommand` messages.
- Application projects may reference `CleanArchitecture.BuildingBlocks.Hosting` for registration only; the building-blocks evolution plan's composition-root-only rule is amended accordingly.
- The first implementation slice is approval, rejection and revocation through Api, Runtime and Executor. Refactoring proceeds by preserved vertical behaviour, not by a repository-wide move.
- The reference system follows this decision selectively, as recorded in the [ADR-011 amendment](ADR-011-reference-system-location.md#internal-composition-amendment-2026-10-29): the deploy controller and scenario runner in full, the workload services for file and naming conventions only, and nothing there references a `Sentinel.*` package.

The detailed conventions and migration sequence are maintained in [Application composition and CQRS refactoring guide](../architecture/application-composition-and-cqrs.md).

## Consequences

### Positive

- Every layer registers the types it owns, making composition discoverable and dependency direction explicit.
- Every process, task and validator of a service is listed under its own root folder, and one operation is the files that carry its name across them.
- Api projects and infrastructure stop accumulating application orchestration.
- Existing logging, validation and duplicate-handler safeguards are reused consistently.
- Architecture and registration rules can be tested across all five services.
- The pattern respects Sentinel's actor, workflow and trust boundaries rather than forcing every runtime concept into a mediator abstraction.

### Negative

- The migration touches all five services and will temporarily contain old and new structures.
- The processor seam adds one type per operation and a request record that often mirrors its command. This is accepted uniformly, for queries too, so that the inbound path is always the same length.
- One type per file multiplies file count; Runtime's incident commands alone become twenty-six files. Navigation moves from scrolling to file names, which is the intent.
- Moving registrations can expose lifetime and dependency-cycle problems that the current broad infrastructure modules hide.
- Moving from Minimal APIs to controllers is churn in every host with no change in enforced properties; it is accepted for cross-project consistency. The server-sent-events endpoint and the tenant endpoint filter need rework as an `IResult`-returning action and an action filter.
- Team members must distinguish application requests, application commands, integration messages and Runtime's aggregate-level incident commands.
- Moving rules into Domain changes where tests live: behaviour currently covered through application services needs domain tests before the move, or the move is unverified.

## Follow-up work

- Complete building-blocks Stage E2: reinstate `IRequest<T>`, `IProcessor<,>` and `IRequestDispatcher`, add the module composer, split the package's own multi-type files, and record each change in its changelog.
- Add StyleCop with the two file rules at warning severity, plus the architecture and DI composition tests, before moving production behaviour.
- Implement the approval-to-execution slice while preserving authorization, attestation, contract and idempotency tests.
- Refactor the remaining services in the sequence documented by the architecture guide.
- Remove transitional `Ports.cs`, `*Services.cs`, `*Adapters.cs` and `*Infrastructure.cs` files, inline endpoint mappings and mixed-layer registrations only after their consumers have migrated; then raise the file rules to error.
- Change this ADR to Accepted after the first slice proves the registration, processor, CQRS and actor/workflow interaction rules without behavioural regression.

## Revisit criteria

Revisit if the first slice shows that the shared dispatcher cannot support required runtime behaviour, if handler indirection materially harms measured performance, or if a layer cannot own its registrations without violating an earlier service, trust or runtime decision. Convenience alone is not sufficient reason to reintroduce Domain DI or mixed-layer registration.
