# Application composition and CQRS guide

## Status and purpose

This is the maintained target design for how a Sentinel service is composed inside its four projects: where a request enters, how it becomes a command or query, which layer registers what, how files and types are organised, and which rules are enforced by tests and analyzers. It changes structure, not Sentinel's topology, contracts, trust boundaries or runtime behaviour.

The design takes its patterns from two reference projects and adapts them to decisions Sentinel has already made:

- `C:\Users\ingle\OneDrive\cleanarchitecure` (the single-service template): layer-owned dependency modules composed in a fixed order, the processor seam between transport and use case, result-to-problem mapping at the edge.
- `C:\Users\ingle\OneDrive\projects\distributed-job-engine` (JobService): feature folders per operation, one interface per abstraction file, domain error catalogues, architecture tests, application-owned diagnostics.

The corresponding decision is [ADR-020](../decisions/ADR-020-application-composition-and-cqrs.md). Sections marked **Current** describe the repository on 2026-10-29; everything else is the target.

## Why this refactor is needed

**Current.** Sentinel's service boundaries are right and its building blocks are capable, but inside each service the composition has drifted:

- Every host calls `AddBuildingBlocks` itself and then registers messaging, so the Application layer does not own its own handlers and validators.
- Every Infrastructure registration method also registers Application services. Runtime's registers fourteen Application types; Api's registers its three application services beside its HTTP clients.
- `Program.cs` files mix composition with route structure and use-case orchestration. Api's is about 210 lines and maps twenty-seven endpoints inline; Runtime's maps sixteen.
- Types are clubbed by convenience rather than by ownership. Api's `ApprovalService.cs` holds three interfaces, five records, an options class, the service and an error catalogue. Api's `ApiInfrastructure.cs` holds the registration method plus thirteen clients, stores and options. Runtime's `Ports.cs` holds fourteen types across six concerns. Runtime's `IncidentCommands.cs` holds twenty-five records, `IncidentEvents.cs` twenty-two, `IncidentModel.cs` eighteen.
- Use cases are procedural service classes (`ApprovalService`, `ExecutionService`, `RemediationControlService`, `ControlPlaneService`) called directly from endpoints and message consumers. The CQRS package exists but nothing dispatches through it.
- Message consumers live in Infrastructure beside HTTP clients and call application services directly, so the inbound path for a message differs from the inbound path for an HTTP request.

The result is that a reader cannot answer, for any feature, the five questions below without opening several large files. The target makes each answer a file name.

1. Where does the request enter? A controller action in the Api project or a consumer in Infrastructure, one file per capability.
2. Which application type represents it? `<Operation>Request` in `BusinessProcesses/<Capability>/`.
3. Which processor accepts it and which command or query does it become? `<Operation>Processor` beside the request; `<Operation>Command` in `BusinessTasks/Commands/<Capability>/` or `<Operation>Query` in `BusinessTasks/Queries/<Capability>/`.
4. Which handler owns the use case and which ports does it need? `<Operation>CommandHandler` or `<Operation>QueryHandler` beside its message; its constructor lists the ports, one interface per file under `Abstractions/`.
5. Which layer registers the implementation? The owning layer's dependency module.

## Request flow

Every inbound request, whether HTTP or an integration message, follows one path:

```mermaid
flowchart LR
    http["Controller action<br/>(Api)"] --> req["&lt;Operation&gt;Request<br/>(Application)"]
    msg["Message consumer<br/>(Infrastructure)"] --> req
    activity["Temporal activity<br/>(Infrastructure)"] --> req
    req --> rd["IRequestDispatcher"]
    rd --> proc["&lt;Operation&gt;Processor<br/>validate, map, dispatch"]
    proc --> cd["ICommandDispatcher / IQueryDispatcher"]
    cd --> h["&lt;Operation&gt;CommandHandler / QueryHandler<br/>the use case"]
    h --> ports["Application ports<br/>IIncidentOwner, IApprovalStore, IOutbox ..."]
    ports --> infra["Infrastructure adapters<br/>grains, stores, clients"]
```

| Step | Owner | Responsibility | Must not |
| --- | --- | --- | --- |
| Controller action or consumer | Api (HTTP), Infrastructure (messages, activities) | Authenticate and authorize, bind transport input and principal into one `<Operation>Request`, send it, map the `Result` to a response or acknowledgement | Touch ports, stores or domain types; contain branching on business state |
| Request | Application | An immutable record of what the caller asked for, in application terms, implementing `IRequest<TResult>` | Carry transport types such as `HttpContext`, `ClaimsPrincipal` or message envelopes |
| Processor | Application | Validate the request with its FluentValidation validator, map it to exactly one command or query, dispatch it, return the result | Call ports, load aggregates, publish, or dispatch more than one message |
| Command or query | Application | The use case's input, named for the business action | Implement any `Sentinel.Contracts` type or be sent across a service boundary |
| Handler | Application | The use case: load through ports, call domain behaviour, persist, enqueue through the outbox, return `Result` | Call another handler; reach Infrastructure types; perform HTTP |
| Ports | Application `Abstractions/` | One interface per external need | Expose SDK types |
| Adapters | Infrastructure | Implement ports with grains, PostgreSQL, Redis Streams, HTTP clients, Temporal | Register Application types |

`IRequestDispatcher.HandleRequestAsync` (so named by the owner on 2026-10-29; `SendAsync` until building blocks `0.1.0-alpha.6`) resolves the processor from the container by the request's runtime type, `IProcessor<TRequest, TResult>` for an `IRequest<TResult>`, through a cached invoker, the same way the command and query dispatchers resolve handlers and the same way the reference template's request handler resolves a processor for a DTO. No endpoint or consumer names a processor type.

The processor is the seam the reference template calls the "processor" and the job engine calls the same. It exists so that an HTTP endpoint and a message consumer for the same use case are two thin adapters over one application entry point, and so that validation happens once, where untrusted input becomes an application message. The cost is one extra type per operation and a request record that often mirrors its command. That cost is accepted uniformly, for queries too, because a path that is sometimes two hops and sometimes three is harder to read than one that is always three.

### Validation placement

Validation runs once per inbound request, in the processor, against the `<Operation>Request`. A command validator is needed only when a command can be built without passing through a processor, which should be rare once consumers and activities also send requests. Domain invariants stay in Domain and return `Result` failures; validators check shape, not business rules.

### Results at the edge

Handlers return `Result` or `Result<T>` with typed `Error`s from a per-capability error catalogue (`ApprovalErrors`, `PolicyErrors`). The Api project maps `Result` to HTTP once, in one place: an abstract `ApiController` base per service, as in both reference projects, with `Match`, `MatchAccepted`, `MatchCreated` and `Problem` choosing `Ok`, `Accepted`, `NoContent`, `NotFound`, `Conflict` or a problem document from `ErrorType`. Actions do not inspect `IsSuccess` inline. Sentinel's existing `ToProblem` extension folds into the base controller.

### HTTP adapters are MVC controllers

Sentinel's Api projects use MVC controllers, not Minimal APIs, so that the template, the job engine and Sentinel share one HTTP convention. **Current:** every host maps endpoints inline in `Program.cs`. Target: one controller per capability under `Api/Controllers/`, deriving from the host's `ApiController`, with attribute routes under a per-host route root, `[Authorize(Roles = ...)]` for authorization, and action filters for cross-cutting checks such as the incident tenant filter. An action is one `HandleRequestAsync` and one `Match`. Actions may return `IResult` where that is the simplest correct answer, which keeps the server-sent-events endpoint on `TypedResults.ServerSentEvents` inside a controller.

## Layer-owned dependency modules

Each layer that has services to register owns one dependency module. The Api project composes them in a fixed order, so composition is readable in one place and testable as a unit.

| Layer | Module | Registers | Does not register |
| --- | --- | --- | --- |
| Domain | none | Nothing. Domain has no services and no reference to `Microsoft.Extensions.DependencyInjection`; the architecture test that restricts Domain to `BuildingBlocks.Core` and `Sentinel.Contracts.Primitives` enforces this. | |
| Application | `ApplicationDependencyModule` | The building-block pipeline for its own assembly (dispatchers, handlers, processors, validators, decorators) and application services that the scan does not discover | Port implementations, options binding, anything that needs `IConfiguration` |
| Infrastructure | `InfrastructureDependencyModule` | Port implementations, options with validation, data sources, messaging, grains, workflows, HTTP clients, hosted services | Application handlers or services |
| Api | `ApiDependencyModule` | `AddControllers` with the JSON options, authentication, authorization, OpenAPI, problem details, action filters, anything only the process needs | Application or Infrastructure types |

The module contract is the existing `IDependencyModule` in `CleanArchitecture.BuildingBlocks.Hosting`:

```csharp
public interface IDependencyModule
{
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
```

Application modules receive the configuration and ignore it. That asymmetry is accepted so that one interface serves every layer and the composition root can treat modules uniformly.

```csharp
// Sentinel.Gateway.Application/ApplicationDependencyModule.cs
public sealed class ApplicationDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddBuildingBlocks(AssemblyReference.Assembly);
}

// Sentinel.Gateway.Infrastructure/InfrastructureDependencyModule.cs
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddApiOptions(configuration)
            .AddApprovalSigning()
            .AddApiPersistence()
            .AddServiceClients(configuration)
            .AddLiveUpdates(configuration);
    }
}

// Sentinel.Gateway.Api/DependencyContainer.cs
internal sealed class DependencyContainer
{
    private static readonly IDependencyModule[] Modules =
    [
        new Application.ApplicationDependencyModule(),
        new Infrastructure.InfrastructureDependencyModule(),
        new ApiDependencyModule()
    ];

    public static void RegisterAll(IServiceCollection services, IConfiguration configuration)
    {
        foreach (IDependencyModule module in Modules)
        {
            module.RegisterServices(services, configuration);
        }
    }
}
```

Order is Application, then Infrastructure, then Api: inner layers first, so an outer layer's `TryAdd` or decoration sees the inner registrations it refines. The reference template registers Infrastructure first; Sentinel reverses it because its infrastructure decorates and replaces application defaults (test doubles, in-process owners), never the other way round.

Infrastructure modules split into private, capability-named methods (`AddApiPersistence`, `AddServiceClients`) each in its own file under `Infrastructure/DependencyInjection/`. The public module stays a short list of calls.

`Program.cs` becomes a process outline:

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSentinelMessaging("api").RegisterApiMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration);

await using WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();
```

`MapDefaultEndpoints` and `MapOperatorEndpoints` come from `Sentinel.ServiceDefaults` and stay as they are; everything the service itself exposes is a controller.

Message registration (`Produces<>` and `Consumes<,>`) moves from `Program.cs` into one `RegisterApiMessages` extension in Infrastructure, beside the consumers it names, because the host should not know which contract types the service exchanges.

### Package reference consequence

`AddBuildingBlocks` and `IDependencyModule` live in `CleanArchitecture.BuildingBlocks.Hosting`, so an Application module must reference that package. The [building-blocks evolution plan](../implementation/building-blocks-evolution.md) now allows this for registration only, and Hosting continues to carry no infrastructure packages and no ASP.NET Core. The alternative, moving registration into the Application package, would give the Application package a FluentValidation and dependency-injection dependency it does not otherwise need, which is the same trade in a different place. The package-side work (the processor types, the module composer, and splitting the package's own clubbed files) is the evolution plan's Stage E2 and precedes the first service slice.

## Service and layer naming

The service **directory** under `services/` is the service's short name, `Sentinel.<Name>/`, the same name its projects carry. **Decided by the owner on 2026-10-29:** the job engine's `<Name>Service` folder suffix was considered and rejected, because it reads poorly beside the project names and the `services/` parent already says what the folder holds. The **projects and namespaces** inside keep the short name with no suffix: `Sentinel.<Name>.Api`, `.Application`, `.Domain`, `.Infrastructure`. The outer layer is named **Api**, not Host, matching both reference projects; solution explorers and file listings sort projects alphabetically, so `Api`, `Application`, `Domain`, `Infrastructure` reads top-down in dependency order, while `Host` lands third and hides the entry point. Every service has an Api project, including those whose Api is mostly health, operator and query endpoints beside a message consumer.

| Today | Target directory | Target projects | Resource and producer name | Configuration root |
| --- | --- | --- | --- | --- |
| `services/Sentinel.Api/` | `services/Sentinel.Gateway/` | `Sentinel.Gateway.Api`, `.Application`, `.Domain`, `.Infrastructure` | `gateway-service` | `Gateway:` |
| `services/Sentinel.Ingestion/` | unchanged | `Sentinel.Ingestion.Api` and so on | `ingestion-service` | `Ingestion:` |
| `services/Sentinel.Runtime/` | unchanged | `Sentinel.Runtime.Api` and so on | `runtime-service` | `Runtime:` |
| `services/Sentinel.AgentWorker/` | unchanged | `Sentinel.AgentWorker.Api` and so on | `agent-worker-service` | `AgentWorker:` |
| `services/Sentinel.Executor/` | unchanged | `Sentinel.Executor.Api` and so on | `executor-service` | `Executor:` |
| `apps/Sentinel.Web/` | unchanged; it is an app, not a service | `Sentinel.Web` | `web` | unchanged |

Only one service changes its short name. `Sentinel.Api` becomes **`Sentinel.Gateway`**, because `Sentinel.Api.Api` is not a usable project name, the architecture already draws the service as the "API / Edge Gateway", it is the one trusted edge for the web app and operators, and the name says nothing false about approval signing, audit, control-plane relay or live events. `ControlPlane` was rejected because the service also serves data-plane reads; `Edge` and `Portal` say less. `AgentWorker` keeps its name; the ADRs use it throughout.

**What changes and what must not.** The rename touches the Gateway's directory, the `.Host` project names and namespaces, the Gateway project names and namespaces, the solution file, the AppHost resource names, configuration section roots and the `Services:` discovery keys, the architecture tests' service list, the steering file, the README, the production-topology document and ADR-001, ADR-006 and ADR-007, and is done in one commit per service with no behaviour change. It must not change anything that is a contract or a durable identity: message type names such as `runtime.incident.opened` and stream names such as `sentinel.executor.commands` are versioned contracts under ADR-012 and stay as they are; the Orleans `ClusterId` and `ServiceId`, the Temporal namespace, task queue and workflow IDs, database and schema names, and the approval attestation `Issuer` and `Audience` are runtime identities that persist across restarts and stay as they are unless a separate migration is planned and recorded. So is the messaging service name passed to `AddSentinelMessaging` (`sentinel-api`, `sentinel-runtime` and so on): it sets the envelope `Producer` and also names the service's Redis consumer groups and inbox records, so renaming it would start new consumer groups at the beginning of every stream and replay every message. The first slice found this and kept it; separating the producer from the consumer identity is a building-block change for later. The architecture tests that forbid cross-service references are updated to the new namespaces in the same commit, so the boundary is enforced before and after.

**Current.** The first slice renamed Gateway, Runtime and Executor: `services/Sentinel.Gateway/`, `services/Sentinel.Runtime/` and `services/Sentinel.Executor/` hold `.Api` projects, with resources `gateway-service`, `runtime-service` and `executor-service`. Ingestion and AgentWorker keep their `.Host` projects and resource names until their own steps.

## Target folder structure

Apply the same shape independently in each service. Create a folder when its first real type exists.

```text
services/Sentinel.<Service>/src/
├── Sentinel.<Service>.Domain/
│   ├── <Aggregate>/
│   │   ├── <Aggregate>.cs
│   │   ├── <ValueObject>.cs            one per file
│   │   ├── <Aggregate>Errors.cs        the error catalogue for the aggregate
│   │   └── Events/
│   │       └── <Something>Happened.cs  one per file
│   └── AssemblyReference.cs
│
├── Sentinel.<Service>.Application/
│   ├── Abstractions/
│   │   ├── Persistence/  I<Thing>Store.cs
│   │   ├── Messaging/    outbound ports beyond the building-block IOutbox
│   │   ├── Providers/    I<External>Queries.cs, I<External>Client.cs
│   │   ├── Runtime/      IIncidentOwner.cs, IIncidentWorkflows.cs
│   │   └── Security/     IApprovalSigner.cs, IApprovalKeyResolver.cs
│   ├── BusinessProcesses/
│   │   └── <Capability>/
│   │       ├── <Operation>Request.cs
│   │       ├── <Operation>Processor.cs
│   │       └── <Operation>Response.cs           what a request returns, when it is a type of its own
│   ├── BusinessTasks/
│   │   ├── Commands/
│   │   │   └── <Capability>/
│   │   │       ├── <Operation>Command.cs
│   │   │       ├── <Operation>CommandHandler.cs
│   │   │       └── <Capability>Mapping.cs       beside the handlers that use it
│   │   └── Queries/
│   │       └── <Capability>/
│   │           ├── <Operation>Query.cs
│   │           └── <Operation>QueryHandler.cs
│   ├── Validators/
│   │   └── <Capability>/
│   │       └── <Operation>RequestValidator.cs
│   ├── Services/         only shared application policy
│   ├── Options/          <Capability>Options.cs, bound by Infrastructure
│   ├── ApplicationDependencyModule.cs
│   └── AssemblyReference.cs
│
├── Sentinel.<Service>.Infrastructure/
│   ├── DependencyInjection/
│   │   ├── InfrastructureDependencyModule.cs
│   │   └── <Capability>Registration.cs          the private Add* methods
│   ├── Messaging/
│   │   ├── <Service>Messages.cs                 Produces/Consumes registration
│   │   └── Consumers/<Contract>Consumer.cs      one per contract, sends a request
│   ├── Persistence/
│   │   ├── <Service>Schema.cs
│   │   └── Postgres<Thing>Store.cs              one per port
│   ├── Clients/          <Peer><Purpose>Client.cs, one per port
│   ├── Grains/, Workflows/, Providers/, Models/  as the service needs
│   └── AssemblyReference.cs
│
└── Sentinel.<Service>.Api/
    ├── Controllers/
    │   ├── ApiController.cs                     abstract base: Match, MatchAccepted, Problem
    │   ├── <Capability>Controller.cs            one per capability
    │   └── Routes.cs                            route roots for this host
    ├── Contracts/
    │   ├── <Operation>Body.cs                   transport bodies, one per file
    │   └── ...
    ├── Filters/                                 action filters, when needed
    ├── DependencyContainer.cs
    ├── ApiDependencyModule.cs
    └── Program.cs
```

### Application layout

**Decided by the owner on 2026-10-29.** An Application project separates what a caller asks from the work that carries it out, each split by capability: `BusinessProcesses/<Capability>/` holds the requests, their processors and what they return; `BusinessTasks/Commands/<Capability>/` and `BusinessTasks/Queries/<Capability>/` hold the commands and queries a processor dispatches, with their handlers; `Validators/<Capability>/` holds the request validators. `Abstractions/`, `Options/` and `Services/` stay at the root. A capability is a business area such as `Approvals`, `Remediation` or `Execution`, the same name in every root folder, so every process, command, query and validator of a service can be listed at a glance and one operation is the files with its name across them. This replaces the job engine's feature folder per operation, which the first draft of this guide followed.

The layout keeps rule 6 checkable by namespace: a request and its processor share `BusinessProcesses.<Capability>`, a command and its handler share `BusinessTasks.Commands.<Capability>`, a query and its handler share `BusinessTasks.Queries.<Capability>`. An error that no aggregate owns sits beside the handlers that return it; aggregate errors stay in Domain (rule 12).

Tests mirror the structure: the approval handlers' tests sit in `BusinessTasks/Commands/Approvals/` and the aggregate's in `Domain/`.

## One type per file

A file contains one top-level type and is named after it. This is the rule the user review asked for, it is what both reference projects do, and it is what makes the five questions above answerable from a file listing.

- Nested private types, and a private record used only by the file's type, may stay in the file.
- A closed polymorphic hierarchy keeps its base type in one file and each derived type in its own file in a folder named for the base: `Incidents/Commands/IncidentCommand.cs`, `Incidents/Commands/OpenIncidentCommand.cs`. The `JsonDerivedType` attributes remain on the base.
- Enums get their own file unless they are nested in the only type that uses them.
- Extension-method classes are one per file and named for what they extend or do.

Enforce it with analyzers rather than review. Add `StyleCop.Analyzers` to `Directory.Packages.props` and enable only two rules in `.editorconfig`: `SA1402` (file may only contain a single type) and `SA1649` (file name must match the first type name), with every other StyleCop rule set to `none`. Introduce them at `warning` severity with a suppression file for the files awaiting migration, and raise to `error` when the migration finishes. Doing this before the split would fail the build on day one.

## Domain modelling

The job engine's `Job` is the model to follow: a sealed aggregate with private setters, a factory, behaviour methods that return `Result`, an explicit transition table in `JobStateMachine`, an error catalogue in `JobErrors`, one enum per file and one domain event per file. Its application handlers load the aggregate, call one method, persist and publish; its infrastructure maps the aggregate to tables and knows nothing about the rules. Sentinel already does this well in two places and badly in several others.

**Current.** `Incident` and `ExecutionRequest` are real aggregates: private state, `Result`-returning operations, error catalogues, a transition table in Executor. The problems are around them. Twelve enums are clubbed into files with the records that use them (`IncidentModel.cs` alone has seven enums and eleven records). `Incident` is 770 lines and carries four lifecycles, investigation, decision, remediation and recovery, with no explicit transition table for any of them. Api's domain is one record and one enum, while the approval rules it should own, hash equality, single decision per plan version, expiry, live in `ApprovalService` in Application. `AgentExecution` and `SourceState` are records with no behaviour. Recovery success is decided by comparing measurements to thresholds in `RecoveryVerificationService` in Application, which is a domain rule.

### Rules

1. **Domain owns every rule that decides an outcome.** If a handler, processor or service contains an `if` or `switch` on domain state that determines success or failure, that decision moves into the aggregate or a domain policy and returns a `Result` with an error from the aggregate's catalogue. Handlers may branch on a `Result`; they may not re-derive the rule.
2. **Aggregates expose behaviour, not setters.** Sealed classes, private or init-only state, a static factory for creation and a static `Rehydrate` for event-sourced ones, and one method per business operation. Collections are exposed read-only. Nothing outside Domain constructs an aggregate in a particular state.
3. **State machines are tables.** Every aggregate with a lifecycle has a `<Aggregate>State` enum in its own file and a `<Aggregate>StateMachine` or private transition table that lists allowed transitions and terminal states, as `JobStateMachine` and `ExecutionRequest.Allowed` do. Operations check the table and return `InvalidState` from the catalogue; they do not check `State ==` inline.
4. **Errors are a catalogue per aggregate.** `<Aggregate>Errors` holds static `Error` values and factories with stable codes (`Incident.NotCurrentPlan`). Application code never constructs an `Error` for a domain condition.
5. **Value objects are records, one per file, with their invariants.** A record that can be constructed invalid (negative version, empty hash) gets a factory that returns `Result`. Records that merely carry data and have no invariant stay plain.
6. **Domain events are one per file under `Events/`**, named in the past tense, carrying identifiers and the facts of the change, never the aggregate. For Runtime's event-sourced stream the event is also the persistence format, so adding a field is additive and the upcaster lives in Infrastructure.
7. **Domain is dependency-free.** Only `BuildingBlocks.Core` and `Sentinel.Contracts.Primitives`, enforced by the existing architecture test. No `Sentinel.Contracts` message or read-model types, no serialization attributes beyond what the event stream requires, no options or clock; time arrives as a parameter.
8. **Application orchestrates.** A command handler loads through a port, calls one aggregate method, persists through the port, enqueues integration messages through the outbox, and returns the aggregate's `Result`. A query handler reads a projection or read port and maps to a response. Mapping domain to responses and contracts is Application's job, one mapper per feature, not a 234-line `IncidentMapping.cs`.
9. **Infrastructure maps persistence.** Rows, JSON documents and events map to and from domain types inside Infrastructure: EF configurations, explicit row mappers, event serializers and upcasters. Infrastructure never evaluates a rule, and Domain never carries a persistence attribute, table name or column name.
10. **Split an aggregate by lifecycle, not by size.** When one aggregate serialises several lifecycles, extract each into a child entity the root delegates to, keeping one root and one stream so ADR-004's single writer is unchanged.

### Applied to Sentinel

| Service | Change |
| --- | --- |
| Runtime | Keep `Incident` as the one root and stream (ADR-004). Extract `Investigation`, `Remediation` (plan, approval, execution) and `Recovery` as child entities under `Domain/Incidents/`, each with its own state enum, transition table and errors; `Incident` delegates and raises the events. Move the measurement-versus-threshold rule from `RecoveryVerificationService` into a `RecoveryCriteria` domain policy that returns `RecoveryResult`. Split `IncidentModel.cs`, `IncidentEvents.cs`, `Consensus.cs` and `CorrelationEngine.cs` one type per file; `CorrelationDecision` and its five cases become a `Correlation/Decisions/` folder. |
| Api | Replace the `ApprovalRecord` record with an `Approval` aggregate: `Approve`, `Reject`, `Revoke` returning `Result`, the hash, version and single-decision rules inside, `ApprovalDecision` and `ApprovalErrors` in their own files. `ApprovePlanCommandHandler` then loads or creates the aggregate, calls one method, signs, saves and publishes. |
| Executor | `ExecutionRequest` stays the model. Split `PolicyEngine.cs` into `Policy/` with one type per file; `PolicyValidator` and `PolicyEngine` are domain policies and stay in Domain. `ExecutionService` shrinks to handlers that call `ExecutionRequest` methods. |
| AgentWorker | Give `AgentExecution` its lifecycle: `Start`, `RecordToolCall`, `Complete`, `Fail`, `Abstain` with an `ExecutionState` table and `AgentExecutionErrors`, so budget and schema-failure rules leave `InvestigationService`. `ToolCallRecord`, `CapturedResponse` and `ExecutionInputs` one per file. |
| Ingestion | `SourceState` gains `Advance`, `MarkFailed` and `Recover` with `SourceHealth` transitions in a table, so `IngestionCycle` stops mutating it field by field. `RedactionPolicy` and `LogSignature` split into their own files. |

## Interface placement

Interfaces live in the layer that needs them, one per file, grouped by the kind of dependency they abstract.

| Interface kind | Location | Examples today |
| --- | --- | --- |
| Persistence a use case needs | `Application/Abstractions/Persistence` | `IIncidentStore`, `ICorrelationStore`, `IApprovalStore`, `IExecutionStore`, `IPolicyStore` |
| Peer-service access | `Application/Abstractions/Providers` | `IObservationQueries`, `IPolicyEvaluator`, `IRuntimeIncidents`, `IStartConfirmation` |
| Runtime coordination | `Application/Abstractions/Runtime` | `IIncidentOwner`, `ICorrelationPartitions`, `IIncidentWorkflows`, `IServiceCatalog` |
| Signing and keys | `Application/Abstractions/Security` | `IApprovalSigner`, `IApprovalKeyResolver` |
| Privileged adapters | `Application/Abstractions/Remediation` | `IRemediationAdapter` |
| Telemetry the application emits | `Application/Abstractions/Diagnostics` | `IIngestionMetrics` |
| Implementation detail | Beside its implementation, `internal` | a Redis codec used by one adapter |
| Transport concern | Api | action filter, the `ApiController` base |

An interface earns its existence by crossing a layer or by being a substitution seam a test uses. A concrete internal class with no second implementation and no boundary crossing stays concrete.

## Message consumers

**Current.** Consumers implement `IIntegrationMessageHandler<T>` in Infrastructure files beside HTTP clients and call application services directly.

Target: one consumer class per contract under `Infrastructure/Messaging/Consumers/`, each a five-line adapter that maps the contract to a `<Operation>Request` and sends it through `IRequestDispatcher`. The same processor and handler then serve HTTP and messages. Consumers stay in Infrastructure because the building-block consumer contract lives there and because the inbox and unit of work are infrastructure concerns.

Integration contracts in `Sentinel.Contracts` never implement `IRequest`, `ICommand` or `IQuery`. The consumer maps.

## Sentinel-specific boundaries

CQRS is the application pattern. It does not replace the actor and workflow models.

- **Orleans.** `IncidentGrain` remains the single writer. A Runtime command handler validates and coordinates, then sends an `IncidentCommand` through `IIncidentOwner`. `IncidentCommand` is the aggregate-level command set serialized to the grain; `ICommand` is the in-process application message. They are different things with different lifetimes, and renaming one to avoid the shared word is not worth the churn. The namespaces `Application.Incidents.Commands` (aggregate) and `Application.BusinessTasks.Commands.*` (application) keep them apart.
- **Temporal.** Workflow code performs no I/O and never touches the container. Activities may send requests through `IRequestDispatcher`; that is the only place an activity reaches the application layer.
- **Long-running operations.** A handler may start a durable operation and return an accepted identifier. It never waits for a workflow to finish.
- **Trust path.** Approval goes Api to Runtime to Executor (ADR-006). Moving the approval use case into a processor and handler changes where the code lives, not who signs, who relays and who verifies.

## Stable strings

Protocol, configuration, route-root, service and telemetry identifiers live as constants beside their owner: `SectionName` on each options class, a `Routes` class per host, message names in `Sentinel.Contracts`, meter and activity-source names beside the instrumentation, roles beside identity. No global constants class. User-facing text, log templates and one-use literals stay where they are.

## Registration and lifetime rules

- Handlers, processors and validators are scoped and registered only through `AddBuildingBlocks` in the Application module. Calling it twice throws, so the Api project must not call it.
- Units of work and the stores that join them are scoped. Clocks, options snapshots and immutable caches are singletons. Hosted services create a scope before resolving scoped types.
- Orleans and Temporal registrations keep their frameworks' lifetimes.
- Options bind in Infrastructure with `ValidateOnStart`. Handlers take `IOptions<T>` or a snapshot, never `IConfiguration`.
- The composition test for each host builds the provider with the host's configuration and asserts: every command, query and request resolves exactly one handler or processor; every port has exactly one implementation; no scoped type is captured by a singleton; no hosted service, consumer or outbox worker is registered twice.

## Worked example: approve a plan

The first slice, chosen because it crosses the public edge, Runtime's authority and Executor's privileged boundary.

**Current.** `POST /api/v1/incidents/{id}/plans/{planId}/approve` is an inline lambda in Api's `Program.cs` that builds an `ApprovePlan` record and calls `ApprovalService.ApproveAsync`, a 311-line file that also declares the three ports it uses, the options class, the receipt and the error catalogue. The approval reaches Runtime as `RecordApproval`, consumed by `RecordApprovalHandler` in `RuntimeAdapters.cs`, which calls `RemediationControlService`.

Target, Api:

```text
Sentinel.Gateway.Api/Controllers/ApprovalsController.cs
    [Route(Routes.Incidents + "/{incidentId:guid}/plans/{planId:guid}")]
    [Authorize(Roles = Roles.Approver)]
    [IncidentTenant]
    public sealed class ApprovalsController(IRequestDispatcher requests) : ApiController
    {
        [HttpPost("approve")]
        public async Task<IResult> Approve(Guid incidentId, Guid planId,
            ApprovePlanBody body, CancellationToken ct) =>
            MatchAccepted(
                await requests.HandleRequestAsync(
                    new ApprovePlanRequest(IncidentId.From(incidentId), PlanId.From(planId),
                        body.PlanVersion, body.PlanHash, User.UserId()), ct),
                Routes.IncidentLocation(incidentId));
    }

Sentinel.Gateway.Application/BusinessProcesses/Approvals/
    ApprovePlanRequest.cs          : IRequest<ApprovalReceipt>
    ApprovePlanProcessor.cs          maps to ApprovePlanCommand and dispatches; validation runs before it
    ApprovalReceipt.cs
Sentinel.Gateway.Application/Validators/Approvals/
    ApprovePlanRequestValidator.cs   approver present; version and hash are judged by the aggregate
Sentinel.Gateway.Application/BusinessTasks/Commands/Approvals/
    ApprovePlanCommand.cs          : ICommand<ApprovalReceipt>
    ApprovePlanCommandHandler.cs     the body of today's ApprovalService.ApproveAsync
    ApprovalMapping.cs
Sentinel.Gateway.Domain/Approvals/
    Approval.cs, ApprovalDecision.cs, ApprovalErrors.cs
Sentinel.Gateway.Application/Abstractions/
    Providers/IRuntimeIncidents.cs
    Security/IApprovalSigner.cs
    Persistence/IApprovalStore.cs
Sentinel.Gateway.Application/Options/ApprovalOptions.cs
```

Target, Runtime:

```text
Sentinel.Runtime.Infrastructure/Messaging/Consumers/RecordApprovalConsumer.cs
    sends new RecordApprovalRequest(message) through IRequestDispatcher
Sentinel.Runtime.Application/BusinessProcesses/Remediation/
    RecordApprovalRequest.cs, RecordApprovalProcessor.cs
Sentinel.Runtime.Application/BusinessTasks/Commands/Remediation/
    RecordApprovalCommand.cs, RecordApprovalCommandHandler.cs
        verifies plan currency, sends ApprovePlanCommand (the IncidentCommand) via IIncidentOwner,
        signals the workflow via IIncidentWorkflows
```

Executor keeps `AttestationVerifier` and the policy engine as they are; its `ExecuteRemediation` consumer becomes a request in `BusinessProcesses/Execution/` and a command in `BusinessTasks/Commands/Execution/`.

The characterization tests that must stay green: approval negative tests (missing, expired, wrong hash, forged, replayed), the Api to Runtime contract test, the duplicate-delivery test, and the end-to-end golden path.

## Scope: the reference system

The reference system under `reference-system/` is not a Sentinel service, and [ADR-011](../decisions/ADR-011-reference-system-location.md#internal-composition-amendment-2026-10-29) applies this guide to it in two levels. The deploy controller and the scenario runner follow it in full, including the layer modules, the processor seam and a reference to the building blocks, because they have real use cases and the deploy controller sits on the golden path. Checkout, orders and the reporting worker follow only the one-type-per-file, folder-per-concern and thin-`Program.cs` rules, so their injected faults stay easy to find. Nothing in the reference system references a `Sentinel.*` package. The reference-system rows in the inventory below reflect that split.

## Current clutter inventory and target placement

The files below are the ones to split first. Counts are top-level types per file on 2026-10-29.

| Service | File | Types | Target |
| --- | --- | --- | --- |
| Api | `Application/Approvals/ApprovalService.cs` | 11 | Three abstractions files, `Options/ApprovalOptions.cs`, `BusinessProcesses/Approvals/` (requests, processors, `ApprovalReceipt.cs`), `BusinessTasks/Commands/Approvals/` and `Validators/Approvals/` for approve, reject and revoke; `ApprovalErrors.cs` in Domain |
| Api | `Infrastructure/ApiInfrastructure.cs` | 14 | `DependencyInjection/` module and registrations; `Clients/` six client files; `Persistence/` three store files plus schema; `Security/LocalApprovalSigner.cs`; `Options/SigningOptions.cs` moves to Application |
| Api | `Infrastructure/LiveInfrastructure.cs` | 13 | `Messaging/Consumers/` six consumers; `Persistence/PostgresLiveEventStore.cs`; live-update registration file |
| Api | `Application/ControlPlane/ApiConfiguration.cs`, `ControlPlaneService.cs` | 8, 8 | `BusinessProcesses/ControlPlane/`, `BusinessTasks/Commands/ControlPlane/` and `BusinessTasks/Queries/ControlPlane/` for the publish, read and history operations |
| Api | `Api/Program.cs` | 27 endpoints | `Controllers/` with `ApiController` plus `IncidentsController`, `ApprovalsController`, `PostmortemsController`, `LiveController`, `AdminController`; `Contracts/` one body per file; `Filters/IncidentTenantAttribute.cs`; `DependencyContainer.cs` |
| Runtime | `Application/Ports.cs` | 14 | `Abstractions/Persistence/` three stores; `Abstractions/Runtime/` four coordination ports; `Abstractions/Providers/` two peer ports; `StoredEvent`, `IncidentSignal`, `ServiceContext` beside their owners |
| Runtime | `Application/Incidents/IncidentCommands.cs` | 25 | `Incidents/Commands/` one file per command plus the base |
| Runtime | `Domain/Incidents/IncidentEvents.cs` | 22 | `Domain/Incidents/Events/` one file per event |
| Runtime | `Domain/Incidents/IncidentModel.cs` | 18 | One value object or enum per file under `Domain/Incidents/`; the seven enums get their own files |
| Runtime | `Domain/Incidents/Incident.cs` | 2, 770 lines | Root stays; `Investigation`, `Remediation` and `Recovery` child entities with their own state enums, transition tables and errors; `IncidentErrors.cs` separate |
| Runtime | `Domain/Correlation/CorrelationEngine.cs`, `Domain/Incidents/Consensus.cs` | 13, 5 | One type per file; `Correlation/Decisions/` for the five decision cases; `SeverityPolicy.cs` and `ConsensusSettings.cs` separate |
| Runtime | `Application/RecoveryVerificationService.cs` | 3 | The threshold rule becomes `Domain/Incidents/RecoveryCriteria.cs`; the service becomes a handler that queries observations and calls it |
| Runtime | `Application/IncidentMapping.cs` | 1, 234 lines | One mapper per feature beside the query handler that uses it |
| Api | `Domain/Approvals/ApprovalRecord.cs` | 2 | `Approval` aggregate, `ApprovalDecision.cs`, `ApprovalErrors.cs`; the rules move out of `ApprovalService` |
| Executor | `Domain/Policy/PolicyEngine.cs` | 10 | One type per file under `Domain/Policy/` (listed again here for the domain split) |
| AgentWorker | `Domain/Executions/AgentExecution.cs`, `ExecutionInputs.cs` | 3, 2 | `AgentExecution` aggregate with lifecycle methods and `ExecutionState`; `ToolCallRecord`, `CapturedResponse`, `ExecutionInputs` one per file |
| Ingestion | `Domain/Sources/SourceState.cs`, `Domain/Observations/RedactionPolicy.cs` | 2, 2 | `SourceState` with transition methods; `SourceHealth.cs`, `LogSignature.cs` separate |
| Runtime | `Application/Incidents/IncidentCommandExecutor.cs` | 5 | `LocalIncidentOwner` and `IncidentLocks` to a test-support or `Runtime/InProcess/` folder; `IncidentMessages` beside the outbox mapping |
| Runtime | `Infrastructure/RuntimeAdapters.cs` | 9 | `Clients/` two files; `Messaging/Consumers/` six consumers; `Workflows/WorkflowReconciler.cs` |
| Runtime | `Infrastructure/RuntimeInfrastructure.cs` | 1, registers 14 Application types | Module plus `DependencyInjection/` registrations; Application registrations move to `ApplicationDependencyModule` |
| Runtime | `Api/Program.cs` | 16 endpoints | `Controllers/` with `ApiController` plus `IncidentsController`, `HistoryController`, `RemediationController`, `ConfigurationController`, `AdminController` |
| Executor | `Application/Ports.cs` | 11 | `Abstractions/Persistence/` three stores; `Abstractions/Security/IApprovalKeyResolver.cs`; `Abstractions/Providers/IStartConfirmation.cs`; `Abstractions/Remediation/` adapter contract and its three result types |
| Executor | `Application/ExecutorServices.cs` | 3 | `Options/ExecutorOptions.cs`; `Policy` and `Execution` capabilities under `BusinessProcesses/` and `BusinessTasks/` for policy evaluation, execution and reconciliation |
| Executor | `Infrastructure/ExecutorInfrastructure.cs` | 8 | Module, `Persistence/`, `Messaging/Consumers/ExecuteRemediationConsumer.cs` |
| Executor | `Domain/Policy/PolicyEngine.cs` | 10 | One type per file under `Domain/Policy/` |
| AgentWorker | `Infrastructure/AgentWorkerInfrastructure.cs` | 7 | Module, `Models/ModelRouter.cs`, `Clients/` two files, `Messaging/Consumers/InvestigateIncidentConsumer.cs`, `Persistence/` schema and store |
| AgentWorker | `Application/Models/ModelContracts.cs` | 9 | `Abstractions/Providers/IModelProvider.cs` plus one file per request and response record |
| AgentWorker | `Application/Tools/InvestigationTools.cs`, `SignalTools.cs` | 8, 5 | One tool per file under `Tools/` |
| Ingestion | `Application/Ports.cs`, `Providers/ProviderContracts.cs`, `SignalProviders.cs` | 4, 6, 8 | `Abstractions/Persistence/`, `Abstractions/Providers/` one interface per file, `Abstractions/Diagnostics/IIngestionMetrics.cs` |
| Reference deploy controller | `Deployments.cs`, `Program.cs` (113 lines, inline endpoints) | 14 | Full template: Domain `Revisions/` aggregate and events; Application `Deployments` (deploy, roll back, reset) and `Services` (get service) capabilities under `BusinessProcesses/` and `BusinessTasks/`; Api `Controllers/`, `DependencyContainer`; may reference the building blocks |
| Reference scenario runner | `ScenarioManifest.cs`, `ScenarioRunner.cs`, `TrafficGenerator.cs` | 15, 5, 2 | Full template: manifest types one per file under Domain `Scenarios/`; Application `Runs` capability (start, stop, reset, stop batch, set traffic) under `BusinessProcesses/` and `BusinessTasks/Commands/`; Api `Controllers/` |
| Reference workload services | `Checkout.cs`, `Orders.cs`, `ReportingBatch.cs`, `Common/Control/FailureControl.cs`, `Common/Redis/RespConnection.cs` | 8, 6, 5, 4, 3 | File rules only: one type per file in folders per concern, with the injected fault in its own clearly named file (`CheckoutSessionLeak.cs`, `SlowOrderLookup.cs`, `ReportingConnectionFanOut.cs`); no layering, no building blocks |

Ingestion is the least cluttered service and the best place to confirm the shape cheaply after the approval slice. The reference deploy controller is the right place to prove the template outside Sentinel's own services, because the end-to-end suite exercises it on every golden-path run.

## Refactoring sequence

Refactor by vertical behaviour and keep the end-to-end path green after each step.

1. **Conventions and safety net.** Accept ADR-020. Complete building-blocks Stage E2: reinstate `IRequest<T>`, `IProcessor<,>` and `IRequestDispatcher` in `CleanArchitecture.BuildingBlocks.Application`, scanned by `AddBuildingBlocks`; add the module composer to Hosting; split the package's own multi-type files. Add StyleCop with the two file rules, at error severity for the package and warning severity for the services. Add the composition test harness. Confirm build, unit tests and the golden path.
2. **Approval slice** across Api, Runtime and Executor as described above, including layer modules and `DependencyContainer` in those three hosts. Delete `ApprovalService` when its three callers are migrated.
3. **Runtime** incident queries, remediation control and the remaining consumers; split `Ports.cs`, `IncidentCommands.cs`, `IncidentEvents.cs`, `IncidentModel.cs`.
4. **Executor** policy evaluation, controls, execution and reconciliation.
5. **Ingestion** queries, knowledge search, source configuration.
6. **AgentWorker** investigation, replay, model configuration.
7. **Api** postmortems, live events, control plane.
8. **Reference system**: deploy controller and scenario runner in full, workload services for file rules only, per ADR-011.
9. **Cleanup.** Remove transitional files, raise the file rules to error, remove the StyleCop suppression file, update the building-blocks evolution plan and this guide's **Current** notes.

Each step: characterization tests first, move the outcome-deciding rules for the capability into the aggregate or a domain policy with their errors and transition table, create the capability folders under `BusinessProcesses/`, `BusinessTasks/Commands/` or `BusinessTasks/Queries/` and `Validators/`, move interfaces without changing semantics, move registrations to the owning module, move endpoints into controllers or consumers into `Messaging/Consumers`, delete the old service when no caller remains, run the service tests and the affected scenario. Domain tests for the moved rules come first, because they are the cheapest tests to write and the ones that outlive the structure. Routes, status codes and response bodies do not change when an endpoint becomes an action; the Playwright and contract tests are the check.

## Enforceable rules

Architecture tests per service, extending the existing `LayerDependencyTests`:

1. Domain references only `BuildingBlocks.Core` and `Sentinel.Contracts.Primitives` (exists).
2. Application does not reference Infrastructure, Api or technology SDKs (exists).
3. Infrastructure does not reference Api (exists).
4. No service references another service or the reference system (exists).
5. Workflow code performs no I/O and reads no clock (exists).
6. Every `ICommand`, `IQuery` and `IRequest` type has exactly one handler or processor in the same namespace.
7. Processors depend only on a validator and a dispatcher.
8. Types implementing `IIntegrationMessageHandler<>` reside in `Infrastructure.Messaging.Consumers` and depend on `IRequestDispatcher`, not on application services.
9. Types in `Api.Controllers` derive from the service's `ApiController`, depend on `IRequestDispatcher` and not on `Abstractions`, application services or `Infrastructure` namespaces, and no `Map(Get|Post|Put|Delete)` call remains in `Program.cs`.
10. No type outside `ApplicationDependencyModule` calls `AddBuildingBlocks`.
11. Domain classes are sealed or abstract, expose no public setters, and every aggregate with a `<Aggregate>State` enum has a transition table in the same folder.
12. Application types do not construct `Error` values for domain codes; every `Error` whose code starts with an aggregate name is declared in that aggregate's `Errors` catalogue in Domain.
13. Domain types carry no persistence, serialization or framework attributes other than the `JsonDerivedType` set on event-stream and aggregate-command base types.

Composition tests per host, as listed under registration rules.

## Definition of done

- Every service has `ApplicationDependencyModule`, `InfrastructureDependencyModule`, `ApiDependencyModule` and a `DependencyContainer`; Domain has none.
- Every inbound HTTP request and integration message enters through a `<Operation>Request` and processor.
- Every outcome-deciding rule lives in a Domain aggregate or policy with its own error catalogue and, where there is a lifecycle, an explicit transition table; handlers orchestrate and Infrastructure maps.
- No `Ports.cs`, `*Services.cs`, `*Adapters.cs` or `*Infrastructure.cs` grab-bag files remain; one type per file is enforced at error severity.
- `Program.cs` in every Api project is composition and middleware only; every service endpoint is an action on a controller deriving from `ApiController`.
- Architecture and composition tests pass in all five services.
- The approval-to-execution path and the golden incident path are green.

## Reference evidence

Reference template (`cleanarchitecure`): `Service.SharedKernel/IDependencyModule.cs`, `Service.Api/DependencyInjection/DependencyContainer.cs`, `Service.Api/Startup.cs`, `Service.Application/ApplicationDependencyModule.cs`, `Service.Application/Processors/*.cs`, `Service.Application/Dispatchers/RequestHandler.cs`, `Service.Api/Controllers/ApiController.cs`.

Job engine (`distributed-job-engine/services/JobService`): `JobService.Application/Jobs/Commands/<Operation>/`, `JobService.Application/Abstractions/*.cs`, `JobService.Domain/Jobs/JobErrors.cs`, `JobService.Domain/Jobs/Events/*.cs`, `JobService.Infrastructure/InfrastructureModule.cs`, `JobService.Api/Program.cs`, `tests/JobService.ArchitectureTests/LayerDependencyTests.cs`. Its empty `DomainModule` is the one pattern deliberately not copied.

Sentinel (`projects/sentinel`): the five `Program.cs` files, the five Infrastructure registration files, the `Ports.cs` files, `ApprovalService.cs`, `ExecutorServices.cs`, `RuntimeAdapters.cs`, the architecture tests and the `CleanArchitecture.BuildingBlocks` changelog, which records the removal of `IDto`, `IProcessor` and `IRequestHandler` in `0.1.0-alpha.2` that this guide reverses with a defined role.
