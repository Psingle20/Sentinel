# Sentinel.Ingestion

Provider connections, canonical normalization and read-only observation queries. Ownership follows the [production topology](../../docs/implementation/production-topology.md).

## Owns

- Provider connections and credentials
- Canonical normalization
- Event-time metadata
- Checkpoints
- Deduplication
- Durable publication
- Read-only observation queries

## Does not own

- Incident decisions
- AI conclusions
- Remediation

## Layout

```text
src/
├── Sentinel.Ingestion.Domain           business rules; references only building-block Core and contract primitives
├── Sentinel.Ingestion.Application      use cases and the ports they need
├── Sentinel.Ingestion.Infrastructure   adapters that implement the ports
└── Sentinel.Ingestion.Host             composition root, endpoints and health checks
tests/
└── Sentinel.Ingestion.Architecture.Tests   layer and service-boundary rules
```

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5110` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `ingestion` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
