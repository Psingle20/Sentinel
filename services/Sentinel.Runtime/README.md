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
├── Sentinel.Runtime.Application      use cases and the ports they need
├── Sentinel.Runtime.Infrastructure   adapters that implement the ports
└── Sentinel.Runtime.Host             composition root, endpoints and health checks
tests/
└── Sentinel.Runtime.Architecture.Tests   layer and service-boundary rules
```

Phase 03 adds `Sentinel.Runtime.Orleans` (grains) and `Sentinel.Runtime.Workflows` (Temporal workflows and activities) as adapter projects beside Infrastructure ([ADR-004](../../docs/decisions/ADR-004-runtime-state-ownership.md), [ADR-005](../../docs/decisions/ADR-005-durable-workflow-engine.md)).

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5120` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `runtime` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
