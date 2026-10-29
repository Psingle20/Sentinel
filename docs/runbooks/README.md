# Runbooks

Operational procedures for running Sentinel. Each runbook says when to use it, the steps, how to verify the result and how to undo it. They describe the local Aspire environment ([ADR-010](../decisions/ADR-010-local-orchestration.md)); a production deployment definition does not exist yet ([Phase 09](../implementation/phase-09-production-hardening.md)), and these will gain its addresses and access paths when it does.

| Runbook | Use it when |
| --- | --- |
| [Emergency stop](emergency-stop.md) | Sentinel must not execute any remediation, now. |
| [Models, spend and demonstrations](models-and-spend.md) | Switching between the test double and live Meta models, preparing a demonstration, or a spend cap was reached. |
| [Dead letters](dead-letters.md) | A message failed repeatedly and was moved to a dead-letter stream. |
| [Projection rebuild](projection-rebuild.md) | Incident views disagree with the event stream, or a projection was lost. |

## Conventions

- Find each service's address in the Aspire dashboard, opened by `dotnet run --project deploy/local/Sentinel.AppHost`. The examples write `$GATEWAY`, `$RUNTIME` and so on for those addresses.
- Gateway calls need an identity with the role named. Outside Development that is a bearer token from the identity provider; locally the Gateway accepts the synthetic development identities in [its configuration](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Api/appsettings.Development.json), chosen with the `X-Sentinel-User` header, for example `operator@example.test` for operator actions. Every mutation needs an `Idempotency-Key` header with a fresh value.
- Never paste a real token, key or customer identifier into a ticket, chat or runbook change.
