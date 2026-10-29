# Sentinel.Gateway

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
├── Sentinel.Gateway.Domain           business rules, such as the Approval aggregate; references only building-block Core and contract primitives
├── Sentinel.Gateway.Application      ports under Abstractions/, BusinessProcesses/, BusinessTasks/Commands/ and Queries/, Validators/, Options/
├── Sentinel.Gateway.Infrastructure   adapters that implement the ports, message registration and consumers, and the layer's module
└── Sentinel.Gateway.Api              controllers, filters, request bodies, identity, the composition and a Program.cs without endpoints
tests/
├── Sentinel.Gateway.Tests                 domain, handler and HTTP characterization tests
└── Sentinel.Gateway.Architecture.Tests    layer, service-boundary, ADR-020 and composition rules
```

The layout follows the [application composition guide](../../docs/architecture/application-composition-and-cqrs.md) (ADR-020); approvals are migrated, the other capabilities follow in the Gateway's own refactoring step.

## Running

Start it with the rest of Sentinel from the project root:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost
```

It listens on `http://localhost:5100` and exposes `/health/live` and `/health/ready`. Its PostgreSQL database is `api` ([ADR-009](../../docs/decisions/ADR-009-persistence-baseline.md)).
