# Sentinel.Executor

The only component that performs privileged actions, under policy, attestation and fencing checks. Ownership follows the [production topology](../../docs/implementation/production-topology.md).

## Owns

- The policy engine
- Capabilities
- Privileged credentials
- Action adapters
- Compensation
- Action verification

## Does not own

- Diagnosis
- Free-form model decisions
- UI approval capture
- The recovery decision

## Layout

```text
src/
├── Sentinel.Executor.Domain           business rules; references only building-block Core and contract primitives
├── Sentinel.Executor.Application      ports, BusinessProcesses/, BusinessTasks/Commands/, and the execution state machine and policy services not yet split
├── Sentinel.Executor.Infrastructure   stores, key resolution, adapters, reconciliation, message registration and consumers, and the layer's module
└── Sentinel.Executor.Api              controllers, request bodies, the composition and a Program.cs without endpoints
tests/
├── Sentinel.Executor.Tests                 execution, attestation and policy tests
└── Sentinel.Executor.Architecture.Tests    layer, service-boundary, ADR-020 and composition rules
```

The layout follows the [application composition guide](../../docs/architecture/application-composition-and-cqrs.md) (ADR-020); execution requests are migrated, policy, controls and reconciliation follow in Executor's own refactoring step.

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5140` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `executor` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
