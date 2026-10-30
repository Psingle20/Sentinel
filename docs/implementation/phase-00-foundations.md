# Phase 00 – Foundations

## Outcome

Create a buildable, observable five-service skeleton and web frontend with Sentinel-owned contracts, strict data ownership and clear dependency rules. This phase establishes the seams that let the product evolve without coupling the domain to Meta, Orleans, a telemetry vendor, a database, or a deployment platform.

## Primary service ownership

All five backend services and `Sentinel.Web`. Phase 00 scaffolds the accepted topology from the [production topology plan](production-topology.md), but implements only the shared foundations needed by the first vertical slice.

## In scope

- Pin .NET 10 LTS in `global.json`, Orleans 10.x, and all dependencies in `Directory.Packages.props` ([ADR-003](../decisions/ADR-003-dotnet-backend-platform.md)). Select and record the persistence baseline, event transport and local orchestration approach.
- Build and evolve `CleanArchitecture.BuildingBlocks` according to the [building-blocks evolution plan](building-blocks-evolution.md), keeping domain primitives separate from application and hosting integrations.
- Create `apps/Sentinel.Web` and the five accepted hosts under `services/`: API, ingestion, runtime, agent worker and executor.
- Scaffold `Sentinel.Web` with Next.js, React and TypeScript; configure Tailwind CSS, shadcn/ui and Radix UI as the only approved general-purpose styling/component stack.
- Establish the [frontend design system](../architecture/frontend-design-system.md): foundation and semantic tokens, CVA variants, dark/light themes, component ownership and documented custom-CSS exceptions.
- Configure Storybook, accessibility checks and Playwright visual-regression coverage before feature-screen expansion.
- Create domain and application modules for tenants, environments, services, SLOs, incidents, observations, evidence, hypotheses, decisions, remediation, approvals, executions and recovery checks, each inside its owning service ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)).
- Define strongly typed identifiers and schema-versioned contracts.
- Define provider SPIs for logs, metrics, traces, topology, alerts, deployments, knowledge, notifications and remediation inside their owning services, not in a shared package (ADR-007).
- Define `IModelProvider` inside `Sentinel.AgentWorker`, with typed agent tools and structured-output validation, without calling a live model yet.
- Establish command/query separation, domain-event conventions and API error conventions.
- Add local configuration, secret references and safe developer defaults; commit no credentials.
- Add OpenTelemetry tracing, metrics and structured logging to the skeleton.
- Add formatting, linting, tests, builds and CI; update `PROJECT.json` commands.
- Establish ADR and schema-change templates.

## Primary deliverables

1. Compiling five-service and web skeleton with explicit internal modules in their owning services (ADR-007): the incident domain in Runtime, providers in Ingestion, agents in AgentWorker, policy and remediation in Executor, and presentation in Api. The only shared packages are the building blocks, `Sentinel.Contracts.Primitives`, `Sentinel.Contracts` and `Sentinel.ServiceDefaults`.
2. Versioned Clean Architecture building-block packages with explicit Core, Application and Hosting dependency boundaries.
3. Versioned Sentinel contract package containing the core contract shapes and IDs, with no domain behavior.
4. Provider and model abstractions with test doubles.
5. Local development bootstrap and health/readiness endpoints.
6. CI pipeline executing build, unit tests, contract checks and linting.
7. ADRs for the building-block package, provider abstraction, model abstraction, persistence baseline and event semantics. Topology, frontend design system, backend platform, runtime state ownership, workflow engine, remediation control path and cross-service access are already recorded in ADR-001 to ADR-007.
8. Frontend component catalogue containing the minimal shell, status, evidence, approval and recovery primitives needed by the first vertical slice.

## Dependency direction

```text
Presentation / Providers / Runtime / Persistence
                    -> Application
                    -> Domain

Domain -> no infrastructure SDKs
Contracts -> stable schemas only
```

The domain must not reference HTTP, Orleans, database, cloud, telemetry-vendor or Meta SDK types.

## Engineering gates

- Architecture tests reject forbidden dependency directions.
- Each service's domain module references only the dependency-free building-block Core package and `Sentinel.Contracts.Primitives`; it does not receive hosting or validation dependencies transitively.
- No service references another service's domain or application module.
- All public contracts carry a schema version and stable identifier types.
- A provider test double can supply observations without vendor SDKs.
- A model test double can return a validated structured response.
- Logs and traces share correlation, tenant and request identifiers.
- Secrets are loaded from local secret configuration or a secret provider, never source control.
- A clean checkout can execute documented setup, build and test commands.
- Frontend checks reject unauthorized UI frameworks and ordinary feature code that bypasses approved tokens or component variants.
- Every reusable frontend component has documented states, keyboard behavior and a Storybook story.

## Test plan

- Unit tests for value objects, identifiers and domain invariants.
- Contract serialization round trips and compatibility snapshots.
- Architecture dependency tests.
- Health/readiness smoke tests.
- Negative tests for malformed configuration and missing secrets.
- Automated accessibility checks and visual-regression baselines for the initial frontend shell and vertical-slice components.

## Risks and controls

| Risk | Control |
| --- | --- |
| Distributed complexity arrives early | Keep each service minimal, automate local orchestration, and complete one cross-service slice before adding breadth. |
| Vendor types leak into domain | Translate only inside adapters and test dependency rules. |
| Contracts churn | Version schemas and require compatibility tests. |
| Framework selection delays delivery | Time-box investigation and record revisit criteria in ADRs. |
| Multiple contributors create visual drift | Enforce semantic tokens, approved components, Storybook review and visual-regression tests. |

## Exit criteria

- The repository builds and tests through repeatable commands.
- The core contracts can represent the complete golden incident lifecycle.
- A fake provider and fake model can drive a minimal incident use case.
- Observability and secret-handling baselines are operational.
- Consequential foundation choices have ADRs and explicit revisit criteria.
- The building-block package satisfies the Phase 00 acceptance criteria in the [evolution plan](building-blocks-evolution.md).
- The frontend foundation satisfies the acceptance criteria in the [design-system plan](../architecture/frontend-design-system.md).

## Deferred deliberately

Live providers, real model calls, action execution and the first cross-service incident path begin in [Phase 01A](phase-01a-walking-skeleton.md). Orleans grains, the workflow engine and complete UI workflows begin in later phases. The frontend foundation and only the components required by the first vertical slice exist here; broader product patterns do not.

