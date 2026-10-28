# Sentinel.AgentWorker

Model-driven investigation through typed tools, returning structured findings to Runtime. Ownership follows the [production topology](../../docs/implementation/production-topology.md).

## Owns

- Meta model calls
- Typed read tools over the Ingestion query contract
- Specialist investigations
- Adversarial verification
- Structured findings

## Does not own

- Authoritative incident writes
- Approvals
- Provider or production credentials
- Direct remediation

## Layout

```text
src/
├── Sentinel.AgentWorker.Domain           business rules; references only building-block Core and contract primitives
├── Sentinel.AgentWorker.Application      use cases and the ports they need
├── Sentinel.AgentWorker.Infrastructure   adapters that implement the ports
└── Sentinel.AgentWorker.Host             composition root, endpoints and health checks
tests/
└── Sentinel.AgentWorker.Architecture.Tests   layer and service-boundary rules
```

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5130` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `agentworker` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
