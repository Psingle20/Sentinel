# Sentinel

## Purpose

Sentinel is an AI-native incident investigation and recovery platform for distributed systems. It correlates telemetry into incidents, sends specialist investigators after the evidence, challenges every hypothesis before it can lead, proposes remediation that policy must permit and a person must approve, and only calls an incident resolved when telemetry shows recovery.

```text
Observe -> Correlate -> Investigate -> Hypothesize -> Challenge
        -> Approve -> Remediate -> Verify -> Learn
```

Sentinel is built against a controlled reference system with three failure scenarios. In the golden scenario a deployment leaks Redis connections: Sentinel must blame the deployment, reject the competing explanations, propose a rollback, wait for approval, execute it through a constrained adapter and verify recovery. In two confounder scenarios the most recent deployment is innocent, and a deterministic deploy-blame baseline runs beside Sentinel on every investigation. Sentinel has to beat the baseline, not just solve the golden case.

Non-goals for the first milestone are broad vendor coverage, unrestricted autonomous production access, a general-purpose ITSM tool, and custom infrastructure such as databases or message brokers.

## Project type

`microservices`

## Status

Sentinel runs end to end on a developer machine. There is no hosted deployment and no CI pipeline yet; both are deliberate for now. The [phase status ledger](docs/implementation/phase-status.md) is the authoritative record of what each phase has delivered, with evidence; the summary below is a guide.

| Area | State |
| --- | --- |
| Foundations, reference system and walking skeleton (Phases 00, 01, 01A) | Implemented. CI is deferred. |
| Telemetry ingestion (Phase 02) | Prometheus, Loki, Jaeger, deploy-controller and runbook providers with freshness, completeness and redaction metadata. |
| Incident runtime (Phase 03) | Event-sourced incidents, Orleans grains as single writers, Temporal workflows, correlation, projections. |
| AI investigation (Phase 04) | Six specialist roles with typed read-only tools, findings validation, a hypothesis blackboard and prompt-injection guardrails. |
| Verification and consensus (Phase 05) | Adversarial verifier with falsifiers, `consensus-v1` with independence families, dissent and abstention. |
| Safe remediation (Phase 06) | Policy as code, autonomy modes, single-use capabilities, approval revocation, compensation and baseline-aware recovery checks. |
| Memory, replay and evaluation (Phase 07) | In progress: incident memory, similar-incident hints, domain replay, captured-input investigation replay and postmortems. |
| Product experience, hardening, scale (Phases 08 to 10) | Not started beyond the Phase 01A screens. |

Models run through Sentinel's own model interface. The default provider is a deterministic test double, so every local run and test is reproducible; the Meta model adapter is used only when it is configured with credentials (see [Getting started](#getting-started)).

## Architecture summary

Five coarse-grained backend services and one web app establish the trust, scaling and failure boundaries from the start ([ADR-001](docs/decisions/ADR-001-production-first-service-topology.md)):

- **Ingestion** is the only service with telemetry-provider credentials. It normalizes logs, metrics, traces, deployments, topology and runbooks into vendor-neutral observations and serves them through a read-only query contract.
- **Runtime** owns authoritative incident state. Each incident is an event stream written by one Orleans grain, and its lifecycle is a Temporal workflow that passes only IDs. Runtime correlates signals, keeps the hypothesis blackboard, runs consensus, plans remediation and verifies recovery.
- **AgentWorker** runs the investigators. Models see only typed tools over Ingestion's query contract, and their output is validated against structured contracts before Runtime decides what counts as evidence.
- **Executor** is the only service with privileged action credentials. It evaluates policy, issues single-use capabilities, runs constrained adapters and compensates.
- **Api** is the external surface: queries, approvals with signed attestations, and the control plane. Approvals travel to Runtime, never straight to Executor ([ADR-006](docs/decisions/ADR-006-remediation-control-path.md)).
- **Sentinel.Web** is the incident, evidence, approval and recovery experience.

Services talk through an outbox, Redis Streams and an inbox, or through bounded request/response contracts; none reaches into another's database. Models propose; they never hold credentials or execute anything. See the [architecture overview](docs/architecture/overview.md), [production topology](docs/implementation/production-topology.md) and [architecture decisions](docs/decisions/README.md).

## Repository map

- `apps/Sentinel.Web`: the Next.js frontend and its design system.
- `services/`: `Sentinel.Api`, `Sentinel.Ingestion`, `Sentinel.Runtime`, `Sentinel.AgentWorker` and `Sentinel.Executor`, each split into Domain, Application, Infrastructure and Host projects, with their tests beside them.
- `packages/`: the only shared code ([ADR-007](docs/decisions/ADR-007-cross-service-code-and-provider-access.md)): `CleanArchitecture.BuildingBlocks`, `Sentinel.Contracts` and `Sentinel.ServiceDefaults`. There is no shared domain package.
- `reference-system/`: the system Sentinel is tested against ([ADR-011](docs/decisions/ADR-011-reference-system-location.md)): checkout, order and reporting services, the deploy controller, the observability stack configuration, scenarios `S0` to `S3`, runbooks and the deploy-blame baseline.
- `deploy/local/`: the .NET Aspire AppHost that runs everything locally ([ADR-010](docs/decisions/ADR-010-local-orchestration.md)) and the end-to-end suites.
- `docs/`: architecture, decisions, implementation phases, business and API contracts.
- `artifacts/`: generated diagrams and reports, with their sources.
- `scripts/`: repeatable build, test and lint automation.
- `.agents/`, `.codex/`, `.claude/`: project steering, commit guidelines and agent adapters.
- `Sentinel.slnx`, `global.json`, `Directory.Build.props` and `Directory.Packages.props` pin the SDK, analyzers and package versions for every project.

## Getting started

Prerequisites:

- .NET SDK 10.0.302 or a later 10.0.3xx patch, as pinned in `global.json`.
- Node.js 24 LTS for `Sentinel.Web`, as pinned in `apps/Sentinel.Web/.nvmrc`.
- Docker with CLI and engine 25.0 or newer, running. Aspire 13.6 rejects older versions. The AppHost starts PostgreSQL, Redis, Temporal, Prometheus, Loki and Jaeger as containers.

From this directory:

1. Run `setup` once to restore .NET packages and install the web app's dependencies.
2. Run `run` to start all five services, the web app and the reference system. The Aspire dashboard opens with every resource's logs, traces, metrics and health.
3. Start a scenario from the `scenario-runner` resource's commands in the dashboard (`Run S1` is the golden scenario), then follow the incident in Sentinel.Web.

Local sign-in uses synthetic development identities such as `responder@example.test`, which exist only in the development configuration. Tokens between the reference system and Sentinel are generated Aspire parameters kept in the AppHost's user secrets; none is stored in the repository.

To investigate with a Meta model instead of the test double, set `Model:Provider` to `meta` for AgentWorker and supply `Model:Meta:ChatCompletionsUri`, `Model:Meta:Model` and `Model:Meta:ApiKey` through user secrets or a secret manager. Never commit model credentials.

## Commands

| Task | Command |
| --- | --- |
| Setup | `powershell -NoProfile -File scripts/check.ps1 -Task setup` |
| Run | `dotnet run --project deploy/local/Sentinel.AppHost` |
| Test | `powershell -NoProfile -File scripts/check.ps1 -Task test` |
| Lint | `powershell -NoProfile -File scripts/check.ps1 -Task lint` |
| Build | `powershell -NoProfile -File scripts/check.ps1 -Task build` |

Each task covers the .NET solution and `Sentinel.Web` and stops at the first failure; on Linux or macOS run the same script with `pwsh`. `build`, `test` and `lint` need no containers, except for the integration tests that start their own with Testcontainers. The .NET build treats analyzer and code-style warnings as errors, so `build` also enforces C# style. `lint` checks C# whitespace, runs ESLint with the design-system rules and typechecks the web app. [Sentinel.Web's README](apps/Sentinel.Web/README.md) lists its own commands.

The end-to-end suites start the whole system and drive real scenarios, so they take around half an hour:

```text
dotnet test deploy/local/Sentinel.EndToEnd.Tests --filter "FullyQualifiedName~GoldenPathTests"
```

Keep this table synchronized with `PROJECT.json` and the scripts.

## Services and packages

- `apps/Sentinel.Web`: incident list and detail with timeline, evidence and approval, on the governed [frontend design system](docs/architecture/frontend-design-system.md) (Next.js, React, TypeScript, Tailwind CSS, shadcn/ui and Radix UI).
- `services/Sentinel.Api`: external API, development authentication and roles, approval capture with signed attestations, revocation, and incident queries.
- `services/Sentinel.Ingestion`: providers, canonical normalization and redaction, event-time watermarks and late data, checkpoints, observation publication and read-only queries behind a bulkhead.
- `services/Sentinel.Runtime`: incident streams, correlation, Orleans grains, Temporal workflows, the hypothesis blackboard, consensus, remediation planning, recovery verification, projections, incident memory and domain replay.
- `services/Sentinel.AgentWorker`: specialist investigators, typed tools, model routing and telemetry, findings validation, the adversarial verifier, the shadow deploy-blame baseline and investigation replay from captured inputs.
- `services/Sentinel.Executor`: policy as code, autonomy modes and operator controls, single-use capabilities, the rollback and batch-pause adapters, and compensation.
- `packages/CleanArchitecture.BuildingBlocks`, `packages/Sentinel.Contracts` and `packages/Sentinel.ServiceDefaults`: results and errors, outbox and inbox messaging over PostgreSQL and Redis Streams, versioned message and view contracts, and shared hosting defaults.

Service-specific logic stays inside its owning service. Services may share infrastructure such as one PostgreSQL server, but never table ownership.

## Documentation

- [Documentation index](docs/README.md)
- [Architecture](docs/architecture/README.md)
- [Phase-wise implementation plan](docs/implementation/README.md) and the [phase status ledger](docs/implementation/phase-status.md)
- [Architecture decisions](docs/decisions/README.md)
- [Business and API contracts](docs/business-contracts/README.md)
- Operational procedures belong in `docs/runbooks/` and arrive with Phase 09.
- Generated diagrams and reports live under `artifacts/` with their sources and provenance, for example the [walking-skeleton comparison report](artifacts/reports/walking-skeleton/comparison.md).

## Verification

Run `build`, `test` and `lint` before completing a change. Each service's architecture tests enforce layer direction and reject dependencies on other services, and the end-to-end suites keep the golden path passing across real process boundaries. Structural changes must also pass the workspace project validator. Tests stay with the service or package they verify.

Commits follow Conventional Commits as described in [`.agents/commit-guidelines.md`](.agents/commit-guidelines.md); validate a message with `.agents/validate-commit-message.ps1` before committing.

## Related learning

No learning-topic links have been established yet.
