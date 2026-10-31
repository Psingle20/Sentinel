# ADR-011: Reference system location

- Status: Accepted
- Date: 2026-10-28; amended 2026-10-29 with the internal composition rule, and 2026-10-30 with the AWS environment layout

## Context

The Phase 01 reference system (checkout service, order service, reporting worker, Redis and a deploy controller) is executable code, but it is not one of Sentinel's five services. It is the system Sentinel observes and remediates, and the permanent end-to-end test fixture. Project policy requires documenting any project-specific top-level directory.

## Considered options

1. A top-level `reference-system/` directory inside the Sentinel project.
2. A separate workspace project under `projects/`.
3. A directory under `deploy/`.

## Decision

Use a top-level **`reference-system/`** directory in the Sentinel project:

```text
reference-system/
├── services/            checkout-service, order-service, reporting-worker
├── deploy-controller/   deploys healthy or faulty versions; target of the rollback adapter
├── scenarios/           S0–S3 manifests and the deploy-blame baseline definition
└── deploy/              its Redis and observability stack configuration
```

- Sentinel code never references reference-system code. Sentinel interacts with it only as it would with a real system: through telemetry providers and the deploy controller's API.
- Failure-control endpoints are disabled outside explicitly marked development and test environments (Phase 01).
- The reference system has its own Redis, separate from Sentinel's messaging Redis ([ADR-008](ADR-008-event-transport.md)).

Option 2 would make Sentinel's end-to-end suite depend on another project. Option 3 would mix application source with deployment configuration.

### Internal composition (amendment, 2026-10-29)

The reference system contains two kinds of code, and the [application composition and CQRS guide](../architecture/application-composition-and-cqrs.md) adopted by [ADR-020](ADR-020-application-composition-and-cqrs.md) applies to them differently.

- **The deploy controller and the scenario runner follow the guide in full.** They are real services with real use cases: deploy, roll back and reset revisions idempotently; start, stop and reset scenario runs; drive traffic. The deploy controller is the target of Executor's rollback adapter, so its correctness is part of the golden path. Each gets Domain, Application and Host projects, layer-owned dependency modules composed by a `DependencyContainer`, requests through processors to handlers, one type per file and a `Program.cs` that is composition and middleware. They may reference `CleanArchitecture.BuildingBlocks`, which carries no Sentinel concepts; this makes them a second, smaller consumer and contributes to the package's extraction evidence ([building-blocks evolution plan](../implementation/building-blocks-evolution.md)).
- **The workload services follow the file and naming conventions only.** Checkout, orders and the reporting worker exist to emit realistic telemetry and to carry a deliberately injected fault: the connection leak in checkout v2.4.1, the slow query in orders v1.8.0, the batch's connection fan-out. A layered CQRS structure around a few hundred lines of simulated workload would bury the fault, which is the one thing a reader must be able to find. They stay single projects with one type per file, a folder per concern, the revision and fault-control code in clearly named files, and a thin `Program.cs`. They do not reference the building blocks.
- **Nothing in the reference system references a `Sentinel.*` package**, including `Sentinel.Contracts` and `Sentinel.ServiceDefaults`. The isolation is symmetric: Sentinel does not know the fixture's code, and the fixture does not know Sentinel's.

The guide's clutter inventory lists the reference-system files to split.

### AWS environment (amendment, 2026-10-30)

[Phase E01](../implementation/phase-e01-aws-reference-environment.md) adds a second reference environment that runs on local AWS, chosen in [ADR-021](ADR-021-local-aws.md). It stays inside this boundary and the AppHost, and follows the two composition levels above; the [proposal](../architecture/aws-reference-environment.md#proposed-repository-layout) gives the full tree.

- `reference-system/deploy/aws/` holds the local AWS configuration and its pinned image digest.
- `reference-system/services/Reference.QueueConsumer/` is a workload like checkout: the file and naming rules only, its fault in a clearly named file behind the control guard, and no building blocks.
- `reference-system/scenarios/Reference.Scenarios.Infrastructure/` is new and holds the environment implementations, `Local` for today's behaviour and `Aws` for the AWS SDK calls, behind `IReferenceEnvironment` in `Reference.Scenarios.Application` and registered by their own dependency module. The scenario runner has had no Infrastructure project until now; the AWS SDK is the first dependency that justifies one.
- Manifests and evaluation packages sit beside the existing ones. Queue-track scenarios built in the existing environment take the next free `S` numbers, from `S6`; AWS-track scenarios keep the `AWS-S` prefix and name the environment, account and region they run in.
- Environment conformance tests live in `reference-system/tests/Reference.Tests/Aws/`, a folder of their own so the namespace does not shadow the hosting `Environments` class, start the pinned local AWS themselves and carry the `Aws` trait.
- In the AppHost, `AwsReferenceEnvironmentExtensions.cs` adds the `local-aws` resource and its workloads only when `Sentinel:ReferenceEnvironment=aws`, and returns `AwsReferenceEnvironmentResources` as `AddReferenceSystem()` returns `ReferenceSystemResources`. The existing environment remains the default.
- AWS types never cross into Sentinel. Ingestion's and Executor's AWS adapters translate to canonical observations and typed actions, no `Sentinel.*` contract carries an AWS type, and replacing local AWS changes only `reference-system/` and the AppHost.

## Consequences

### Positive

- The fixture is versioned with the scenarios and tests that depend on it.
- The boundary is visible: nothing in `services/` may reference `reference-system/`.

### Negative

- The Sentinel project contains code that is not part of the product; architecture tests must enforce the separation in both directions, including that no reference-system project references a `Sentinel.*` assembly.
- Two composition levels inside one directory need the rule above to be stated at the top of `reference-system/README.md`, or contributors will apply the full guide to the workload services.

## Revisit criteria

Revisit if the reference system grows into a reusable fixture for other projects.
