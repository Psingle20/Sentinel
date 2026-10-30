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
| `GET /v1/executions?incidentId=`, `GET /v1/executions/{id}` | AgentWorker | Api, operators, evaluation | Agent executions with tool calls, token usage, estimated cost and findings | Includes the shadow deploy-blame baseline. |
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

## The Gateway's external HTTP API

`Sentinel.Gateway` serves the web app and future clients under `/api/v1`, documented by the OpenAPI document at `/openapi/v1.json`. The web app calls it only from its server, never from the browser.

### Conventions

- **Identity and roles.** Every route needs a bearer token; locally, a development identity is accepted only in the Development environment. The roles are `responder` (read incidents, write postmortem narratives), `approver` (decide plans) and `operator` (change the control plane). Approval routes need `responder` and `approver`; control-plane routes need `responder` and `operator`.
- **Tenant scoping.** A caller sees only incidents of the tenants in its token. An incident of another tenant answers `404`, not `403`, so its existence is not disclosed. The live stream and the incident list are filtered the same way.
- **Idempotency.** Every `POST`, `PUT`, `PATCH` and `DELETE` under `/api/v1`, except the analytics beacon, needs an `Idempotency-Key` header of 1 to 128 letters, digits, `-`, `_`, `:` or `.`; without one it answers `400 Idempotency.KeyRequired`. Keys are per caller. A repeat with the same body replays the stored answer with `Idempotent-Replayed: true`; the same key with a different body answers `422 Idempotency.KeyReused`, and one still running answers `409 Idempotency.InProgress`.
- **Errors.** Failures are RFC 9457 problem details whose `title`, and `code` extension, is a stable code such as `Approval.ExecutionStarted` or `Configuration.VersionConflict`, with a human-readable `detail`. Every service maps refusals the same way: `400` for a malformed request, `404` for something that does not exist, `409` for a conflict with the current state and `422` for a well-formed request the rules refuse. Control-plane routes relay the owning service's status, code and detail.
- **Versions.** Mutable resources name the version a change starts from (`planVersion`, `expectedVersion`) and are refused with `409` when it is not current.

### Incidents and approvals

| Endpoint | Role | Purpose |
| --- | --- | --- |
| `GET /api/v1/incidents?limit=` | responder | Incident list, newest first; `limit` defaults to 50. |
| `GET /api/v1/incidents/{incidentId}` | responder | Incident detail: timeline, evidence, hypotheses, decision, plan, execution, recovery |
| `GET /api/v1/incidents/{incidentId}/timeline?before=&limit=` | responder | One page of the timeline, newest first, as `{ items, total, next }`. `before` is the stream version to page back from, taken from `next`; `limit` is 1 to 200, default 50. |
| `GET /api/v1/incidents/{incidentId}/evidence?offset=&limit=` | responder | One page of evidence as `{ items, total, next }`, where `next` is the following offset; `limit` is 1 to 200, default 50. |
| `GET /api/v1/incidents/{incidentId}/run-record` | responder | The exported run record |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/approve` | approver | Body `{ planVersion, planHash }`. Signs a single-use ES256 attestation bound to the plan and relays it to Runtime. |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/reject` | approver | Body `{ planVersion, reason }`. Signed and relayed like an approval. |
| `POST /api/v1/incidents/{incidentId}/plans/{planId}/revoke` | approver | Body `{ planVersion, reason }`. Withdraws an approval before execution starts (Phase 06). Asks Runtime synchronously and answers `202` only when Runtime accepted; `409 Approval.ExecutionStarted` or `Approval.RevocationRefused` when it did not, and `503 Approval.RuntimeUnavailable` when it could not be asked. |
| `GET /api/v1/incidents/{incidentId}/postmortem` | responder | The generated postmortem with the human-written narrative (Phase 07). |
| `PUT /api/v1/incidents/{incidentId}/postmortem/narrative` | responder | Body `{ markdown, expectedVersion }`. `409` when someone else saved first. |
| `GET /api/v1/incidents/{incidentId}/similar` | responder | Similar remembered incidents, with provenance. |
| `POST /api/v1/incidents/{incidentId}/replays/domain` | responder | Level-1 domain replay. |
| `GET /api/v1/incidents/{incidentId}/agent-executions` | responder | The incident's agent executions, for the agent activity view. |
| `POST /api/v1/agent-executions/{executionId}/replays` | responder | Level-2 or level-3 replay of one execution. |
| `GET /api/v1/me` | responder | The caller's identity and roles |
| `GET /.well-known/sentinel/approval-keys` | none | The public keys that verify decision and operator attestations, for Runtime and Executor. |

The approval attestation's claims and format are defined in [ADR-006](../decisions/ADR-006-remediation-control-path.md) and `ApprovalAttestationClaims`.

### Live updates

`GET /api/v1/events` (responder) is a server-sent event stream of incident changes for the caller's tenants. Each event is named `incident`, has the sequence number as its `id`, and carries `{ sequence, tenantId, environmentId, incidentId, kind, streamVersion, summary, severity, occurredAt }`. An event says what changed, not the new state: the client refetches the incident from the query API, which stays the source of truth, so a missed event never loses state.

A client resumes with the standard `Last-Event-ID` header, or with `?after={sequence}` when it reconnects itself; without either, the stream starts from now. The web app proxies the stream unchanged, reconnects with backoff from the last event it saw, and polls while it cannot connect.

### Control plane

Each change is authorized here, carried to the owning service, validated there and audited here, refused changes included. Policy and control changes also carry an operator attestation, which Executor verifies before acting.

| Endpoint | Owner | Purpose |
| --- | --- | --- |
| `GET /api/v1/admin/policy` | Executor | The remediation policy in force. |
| `GET /api/v1/admin/policy/history` | Executor | Every policy version: `{ version, author, publishedAt, source }`, the repository seed included. |
| `PUT /api/v1/admin/policy` | Executor | Body `{ document, expectedVersion }`. The document needs a new `version`; `409 Policy.VersionConflict` when the policy changed since it was read. |
| `GET /api/v1/admin/controls` | Executor | `{ emergencyStop, disabledActions }`. |
| `POST /api/v1/admin/controls/emergency-stop` | Executor | Body `{ enabled }`. Engaged, Executor denies every action, approved ones included. |
| `POST /api/v1/admin/controls/actions/{actionKind}` | Executor | Body `{ enabled }`, where `true` takes the action kind (`rollback`, `pause-batch`) out of service and `false` returns it to policy. |
| `GET /api/v1/admin/sources` | Ingestion | Each provider source with its health and watermark. |
| `GET /api/v1/admin/configuration` | Gateway | The configuration areas: `runtime`, `models`, `sources` and `api`. |
| `GET /api/v1/admin/configuration/{area}` | Area's service | `{ document, version, published }`, where `version` is `{ version, author, publishedAt, basedOn }` and `published` is false while the service runs on its deployment's defaults. |
| `GET /api/v1/admin/configuration/{area}/history` | Area's service | Every published version, newest first, ending with the deployment's defaults, whose author is `deployment configuration`. |
| `PUT /api/v1/admin/configuration/{area}` | Area's service | Body `{ document, expectedVersion }`. Refused with `409 Configuration.VersionConflict` when another version was published since, `422 Configuration.VersionUnchanged` without a new version, `409 Configuration.DuplicateVersion` for a version used before and `422 Configuration.Invalid` with the owner's validation problems; a body that is not a document of the area's shape is `400`. |
| `GET /api/v1/admin/audit?limit=` | Gateway | Control-plane changes, newest first, default 100. |
| `GET /api/v1/admin/analytics?days=` | Gateway | Daily product-analytics counts for the last 1 to 366 days, default 30. |

### Product analytics

`POST /api/v1/analytics` (responder) records one event `{ name, properties }` from the approved catalog and answers `202`; anything outside it is refused. It needs no idempotency key, because it only increments a daily count. The catalog and its privacy rules are in [product analytics](product-analytics.md).
