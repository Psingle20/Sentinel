# Synchronous contracts

Bounded request/response calls between services ([ADR-012](../decisions/ADR-012-event-semantics.md)). Bodies use the contract JSON rules: camelCase, enums as camelCase strings, nulls omitted.

| Call | Served by | Called by | Contract | Notes |
| --- | --- | --- | --- | --- |
| `POST /v1/observations/query` | Ingestion | AgentWorker tools, Runtime recovery verification | `ObservationQuery` → `ObservationQueryResult` | Tenant-scoped. Metadata gives `observedThrough`, `freshness`, `completeness`, `truncated`, provider health, redaction policy and a continuation token (ADR-007). |
| `GET /v1/sources` | Ingestion | Operators | Source health and watermarks | Diagnostic. |
| `POST /v1/policy/evaluations` | Executor | Runtime | `PolicyEvaluationRequest` → `PolicyEvaluation` | Advisory plan-time evaluation; execution time is authoritative (ADR-006). |
| `POST /v1/remediation/start-confirmations` | Runtime | Executor | `RemediationStartRequest` → `RemediationStartResponse` | The point of no return. Refused unless the plan is current and the fencing token matches; Executor aborts when Runtime is unreachable. |
| `GET /v1/incidents`, `GET /v1/incidents/{id}`, `GET /v1/incidents/{id}/run-record` | Runtime | Api | `IncidentSummary`, `IncidentDetail`, `IncidentRunRecord` | Rebuildable projections of the incident stream. |
| `GET /.well-known/sentinel/approval-keys` | Api | Executor | `ApprovalKeySet` (JWK) | Public verification keys only. |
| `GET /v1/executions?incidentId=` | AgentWorker | Operators, evaluation | Agent executions with tool calls and token usage | Includes the shadow deploy-blame baseline. |
| `GET /v1/executions` | Executor | Operators | Execution request snapshots | The execution audit trail. |

## Api's external HTTP API

`Sentinel.Api` serves the web app and future clients under `/api/v1`, documented by the OpenAPI document at `/openapi/v1.json`.

| Endpoint | Role | Purpose |
| --- | --- | --- |
| `GET /api/v1/incidents` | responder | Incident list |
| `GET /api/v1/incidents/{incidentId}` | responder | Incident detail: timeline, evidence, hypotheses, decision, plan, execution, recovery |
| `GET /api/v1/incidents/{incidentId}/run-record` | responder | The exported run record |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/approve` | approver | Body `{ planVersion, planHash }`. Signs a single-use ES256 attestation bound to the plan and relays it to Runtime. |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/reject` | approver | Body `{ planVersion, reason }` |
| `GET /api/v1/me` | responder | The caller's identity and roles |

The approval attestation's claims and format are defined in [ADR-006](../decisions/ADR-006-remediation-control-path.md) and `ApprovalAttestationClaims`.
