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
├── Sentinel.Executor.Application      use cases and the ports they need
├── Sentinel.Executor.Infrastructure   adapters that implement the ports
└── Sentinel.Executor.Host             composition root, endpoints and health checks
tests/
└── Sentinel.Executor.Architecture.Tests   layer and service-boundary rules
```

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5140` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `executor` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
