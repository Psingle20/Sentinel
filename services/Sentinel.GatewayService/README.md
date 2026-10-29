# Sentinel.Api

The external API and control-plane edge: authentication, authorization, queries, approvals and live UI delivery. Ownership follows the [production topology](../../docs/implementation/production-topology.md).

## Owns

- External HTTP/API boundary
- Identity and authorization
- Control-plane commands
- Incident queries
- Approval capture and attestation signing
- Live UI delivery

## Does not own

- Telemetry normalization
- Authoritative incident state
- Model execution
- Privileged remediation
- Commands to Executor

## Layout

```text
src/
├── Sentinel.Api.Domain           business rules; references only building-block Core and contract primitives
├── Sentinel.Api.Application      use cases and the ports they need
├── Sentinel.Api.Infrastructure   adapters that implement the ports
└── Sentinel.Api.Host             composition root, endpoints and health checks
tests/
└── Sentinel.Api.Architecture.Tests   layer and service-boundary rules
```

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5100` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `api` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
