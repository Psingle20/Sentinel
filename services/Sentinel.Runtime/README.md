# Sentinel.Runtime

The authoritative incident state and the orchestration of investigation, remediation and recovery. Ownership follows the [production topology](../../docs/implementation/production-topology.md).

## Owns

- Authoritative incident state
- Correlation
- Orleans actors
- Durable workflows
- Evidence and hypotheses
- Consensus
- Projections
- Recovery verification

## Does not own

- Provider credentials
- Direct model SDK use
- Privileged action execution

## Layout

```text
src/
├── Sentinel.Runtime.Domain           business rules; references only building-block Core and contract primitives
├── Sentinel.Runtime.Application      ports under Abstractions/, BusinessProcesses/, BusinessTasks/Commands/, Services/, and the use cases not yet split
├── Sentinel.Runtime.Infrastructure   stores, grains (Grains/), workflows (Workflows/), clients, message registration and consumers, and the layer's module
└── Sentinel.Runtime.Api              controllers, request bodies, the composition and a Program.cs without endpoints
tests/
├── Sentinel.Runtime.Tests                 domain, application, grain and workflow tests
└── Sentinel.Runtime.Architecture.Tests    layer, service-boundary, workflow, ADR-020 and composition rules
```

The layout follows the [application composition guide](../../docs/architecture/application-composition-and-cqrs.md) (ADR-020); approvals are migrated, the rest of the control path, queries and memory follow in Runtime's own refactoring step. Grains and workflows sit in Infrastructure ([ADR-004](../../docs/decisions/ADR-004-runtime-state-ownership.md), [ADR-005](../../docs/decisions/ADR-005-durable-workflow-engine.md)).

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5120` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `runtime` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
