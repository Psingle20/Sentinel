# Synchronous contracts

Bounded request/response calls between services ([ADR-012](../decisions/ADR-012-event-semantics.md)). Bodies use the contract JSON rules: camelCase, enums as camelCase strings, nulls omitted.

## Between services

| Call | Served by | Called by | Contract | Notes |
| --- | --- | --- | --- | --- |
| `POST /v1/observations/query` | Ingestion | AgentWorker tools, Runtime recovery verification | `ObservationQuery` → `ObservationQueryResult` | Tenant-scoped. Metadata gives `observedThrough`, `freshness`, `completeness`, `truncated`, provider health, redaction policy and a continuation token (ADR-007). Behind a bulkhead: `503` when its queue is full. |
| `POST /v1/knowledge/search` | Ingestion | AgentWorker's runbook tool | `KnowledgeQuery` → `KnowledgeResult` | Runbook passages are reference text, never evidence or instructions. |
| `GET /v1/sources` | Ingestion | Operators | Source health and watermarks | Diagnostic. |
| `POST /v1/policy/evaluations` | Executor | Runtime | `PolicyEvaluationRequest` → `PolicyEvaluation` | Advisory plan-time evaluation with the decision's confidence and the incident's severity; execution time is authoritative (ADR-006). |
| `GET /v1/policy` | Executor | Operators | The current `PolicyDocument` | Versioned policy as code. |
| `POST /v1/remediation/start-confirmations` | Runtime | Executor | `RemediationStartRequest` → `RemediationStartResponse` | The point of no return. Refused unless the plan is current, still approved and the fencing token matches; Executor aborts when Runtime is unreachable. |
| `POST /v1/remediation/revocations` | Runtime | Gateway | `RecordRevocation` → `RevocationResponse` | Withdraws an approval (ADR-006 amendment of 2026-10-29). The body carries the Gateway's `revoke-approval` attestation, which Runtime verifies before the incident's writer decides. Accepted only before `ExecutionStarted`; the reason of a refusal is final. The Gateway records the revocation only on acceptance. Runtime side pending while the Gateway client exists. |
| `GET /v1/incidents`, `GET /v1/incidents/{id}` | Runtime | Api | `IncidentSummary`, `IncidentDetail` | Rebuildable projections of the incident stream. |
| `GET /v1/incidents/{id}/state` | Runtime | Api, operators | `IncidentStateView` | The writer's authoritative version beside the projection's: projection lag made visible. |
| `GET /v1/incidents/{id}/run-record?dataset=` | Runtime | Api, the end-to-end suite | `IncidentRunRecord` schema 2 | The projection, the version manifest and the incident's memory (Phase 07). Schema-1 records stay readable. |
| `GET /v1/incidents/{id}/postmortem` | Runtime | Api | `Postmortem` | Generated from the stream; every fact names its sources (Phase 07). |
| `GET /v1/incidents/{id}/similar` | Runtime | Api | `SimilarIncident[]` | Tenant- and retention-scoped retrieval with the shared features as provenance (ADR-019). |
| `GET /v1/memory/{id}` | Runtime | Api, operators | `IncidentMemoryView` | A deleted record returns its tombstone. |
| `POST /v1/incidents/{id}/replays/domain` | Runtime | Api | `DomainReplayResult` | Level-1 replay. Read-only: nothing is written, sent or signalled. |
| `GET /.well-known/sentinel/approval-keys` | Api | Executor | `ApprovalKeySet` (JWK) | Public verification keys only. |
| `GET /v1/executions?incidentId=`, `GET /v1/executions/{id}` | AgentWorker | Api, operators, evaluation | Agent executions with tool calls, token usage and findings | Includes the shadow deploy-blame baseline. |
| `POST /v1/executions/{id}/replays` | AgentWorker | Api | `InvestigationReplayRequest` → `InvestigationReplayResult` | Levels 2 and 3, served only from the execution's captured inputs; `400` for another level or an unknown prompt version (ADR-019). |
| `GET /v1/executions` | Executor | Operators | Execution request snapshots | The execution audit trail. |

### Operator endpoints

Served only in Development or when `Sentinel:OperatorEndpoints:Enabled` is set. They are unauthenticated until Phase 09 adds operator authorization.

| Call | Served by | Purpose |
| --- | --- | --- |
| `POST /admin/projections/rebuild` | Runtime | Rebuilds every projection from its stream; overwrites in place and deletes nothing. |
| `POST /admin/memory/{id}/legal-hold` | Runtime | Body `{ hold, actor }`. Places or lifts a legal hold on an incident's memory. |
| `POST /admin/memory/{id}/delete` | Runtime | Body `{ actor, reason }`. Privacy deletion: a tombstone replaces the record and its features leave the index. Refused under a legal hold. |
| `POST /admin/memory/purge` | Runtime | Body `{ actor, dryRun, limit }`. The retention purge; a dry run unless `dryRun` is `false`. |
| `GET /admin/controls`, `POST /admin/controls/emergency-stop`, `POST /admin/controls/actions/{actionKind}`, `PUT /admin/policy` | Executor | Operator controls and policy publication independent of every model and workflow path (ADR-006). Every mutation requires the header `X-Sentinel-Operator-Attestation`: a Gateway-signed `OperatorAttestationClaims` token for the action's scope and resource over a hash of the submitted content (`PolicyPublicationContent` or `ControlChangeContent`), single-use, about two minutes long. Executor records the actor the claims name; the body's `author` or `actor` is informational. Without it: `401`. |

## Api's external HTTP API

`Sentinel.Api` serves the web app and future clients under `/api/v1`, documented by the OpenAPI document at `/openapi/v1.json`.

| Endpoint | Role | Purpose |
| --- | --- | --- |
| `GET /api/v1/incidents` | responder | Incident list |
| `GET /api/v1/incidents/{incidentId}` | responder | Incident detail: timeline, evidence, hypotheses, decision, plan, execution, recovery |
| `GET /api/v1/incidents/{incidentId}/run-record` | responder | The exported run record |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/approve` | approver | Body `{ planVersion, planHash }`. Signs a single-use ES256 attestation bound to the plan and relays it to Runtime. |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/reject` | approver | Body `{ planVersion, reason }` |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/revoke` | approver | Body `{ planVersion, reason }`. Withdraws an approval before execution starts (Phase 06). Asks Runtime synchronously and answers `202` only when Runtime accepted; `409 Approval.ExecutionStarted` or `Approval.RevocationRefused` when it did not, and `503 Approval.RuntimeUnavailable` when it could not be asked. |
| `GET /api/v1/incidents/{incidentId}/postmortem` | responder | The generated postmortem with the human-written narrative (Phase 07). |
| `PUT /api/v1/incidents/{incidentId}/postmortem/narrative` | responder | Body `{ markdown, expectedVersion }`. `409` when someone else saved first. |
| `GET /api/v1/incidents/{incidentId}/similar` | responder | Similar remembered incidents, with provenance. |
| `POST /api/v1/incidents/{incidentId}/replays/domain` | responder | Level-1 domain replay. |
| `GET /api/v1/incidents/{incidentId}/agent-executions` | responder | The incident's agent executions, for the agent activity view. |
| `POST /api/v1/agent-executions/{executionId}/replays` | responder | Level-2 or level-3 replay of one execution. |
| `GET /api/v1/me` | responder | The caller's identity and roles |

The approval attestation's claims and format are defined in [ADR-006](../decisions/ADR-006-remediation-control-path.md) and `ApprovalAttestationClaims`.
