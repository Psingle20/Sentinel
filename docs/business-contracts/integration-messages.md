# Integration messages

Every message travels in the building-block envelope over Redis Streams, with tenant and environment identity headers, through the producer's outbox and the consumer's inbox ([ADR-008](../decisions/ADR-008-event-transport.md), [ADR-012](../decisions/ADR-012-event-semantics.md)). Shapes are the records in `packages/Sentinel.Contracts`; snapshots under `packages/Sentinel.Contracts/tests/Sentinel.Contracts.Tests/Snapshots` fix their wire form.

## Events

| Message | Schema | Stream | Producer | Consumers | Meaning |
| --- | --- | --- | --- | --- | --- |
| `ingestion.observation.recorded` | 1 | `sentinel.observations` | Ingestion | Runtime | A new canonical metric, deployment or topology observation was stored ([ADR-018](../decisions/ADR-018-observation-publication-scope.md): logs and traces are served by the query contract instead). Deterministic IDs make re-ingestion a duplicate. |
| `runtime.incident.opened` | 1 | `sentinel.incidents` | Runtime | (Api, later phases) | An incident was detected; carries trigger and linked deployment observation IDs and the stream version. |
| `runtime.incident.status-changed` | 1 | `sentinel.incidents` | Runtime | (Api, later phases) | The incident's lifecycle status changed, including reopening. |
| `runtime.incident.blackboard-updated` | 1 | `sentinel.incidents` | Runtime | (Api live views) | An investigation's findings were accepted onto the evidence blackboard; carries counts and the merged hypothesis keys, never the findings themselves. |
| `runtime.decision.recorded` | 1 | `sentinel.incidents` | Runtime | (later phases) | Runtime diagnosed or abstained, with confidence, completeness and calibration. |
| `runtime.remediation-plan.proposed` | 1 | `sentinel.incidents` | Runtime | (later phases) | A versioned, hashed plan with Executor's plan-time policy evaluation. |
| `runtime.recovery.evaluated` | 1 | `sentinel.incidents` | Runtime | (later phases) | Recovery was judged from telemetry. |
| `agentworker.investigation.completed` | 1 | `sentinel.investigations` | AgentWorker | Runtime | Findings (or abstention) for one investigation, with the investigator's role, keyed hypotheses, unknowns, the tool catalog version and, from the verifier, challenges. Proposals until Runtime accepts them. Added fields are optional and omitted when empty, so schema 1 still holds. |
| `executor.remediation.executed` | 1 | `sentinel.remediation` | Executor | Runtime | The execution's result. `outcomeUnknown` is followed by a final result after reconciliation. |

## Commands

| Message | Schema | Stream | Sender | Handler | Meaning |
| --- | --- | --- | --- | --- | --- |
| `agentworker.investigate-incident` | 1 | `sentinel.agentworker.commands` | Runtime | AgentWorker | Investigate one question within a budget and time window, as a specialist role, with the symptom onset; the verifier also receives the hypotheses to challenge and the accepted evidence. From Phase 07 it may carry similarIncidents: remembered incidents of the same tenant with their shared features, as hints that are never evidence (ADR-019). Optional and omitted when empty, so schema 1 still holds. |
| `runtime.record-approval` | 1 | `sentinel.runtime.commands` | Gateway | Runtime | An approver approved an exact plan version; carries the Gateway's signed `execute-plan` attestation, which Runtime verifies before recording (ADR-006 amendment of 2026-10-29). |
| `runtime.record-rejection` | 1 | `sentinel.runtime.commands` | Gateway | Runtime | An approver rejected a plan version, with a reason. Since 2026-10-29 carries the Gateway's signed `reject-plan` attestation in the optional `attestation` field; Runtime refuses a rejection without one. Additive within schema 1. |
| `runtime.record-revocation` | 1 | `sentinel.runtime.commands` | retired | Runtime | Retired on 2026-10-29: a revocation is the bounded request `POST /v1/remediation/revocations` (see synchronous contracts), because its answer must be authoritative. The Gateway no longer produces the message; the type remains as the request body. |
| `executor.execute-remediation` | 1 | `sentinel.executor.commands` | Runtime | Executor | Run the approved plan with its attestation, fencing token and idempotency key. Api never sends it (ADR-006). |

## Rules for consumers

- Reject a delivery whose `sentinel-tenant-id` or `sentinel-environment-id` header differs from the payload (implemented once in `Sentinel.ServiceDefaults`).
- Handle duplicates as no-ops: the inbox records each message ID with the handler's effects.
- A message whose effect the receiving aggregate refuses (for example an approval for a superseded plan) is logged and acknowledged, never retried; transient failures are retried and dead-lettered after the maximum deliveries.
