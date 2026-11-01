# Sentinel implementation review — 2026-10-29

## Verdict

**Changes requested.** This is a substantial implementation, not a superficial scaffold: the five-service topology is intact, ownership boundaries are mostly recognizable, and meaningful Phase 06–08 capability now exists across remediation, replay/memory/evaluation and the product control plane. However, the current working tree is not ready to be treated as a stable phase checkpoint because the canonical build fails and seven confirmed high-severity findings affect execution correctness, operator decisions, idempotency, authentication and tenant isolation.

The right conclusion is **continue from this foundation, but close the safety/correctness slice before broadening it**. The problems are repairable and do not invalidate the architecture.

## Scope and method

- Reviewed the whole maintained project at revision `9d7bfccb8a1bdb6aa0c788a8098f9636e80bc0a9` plus the uncommitted working tree.
- Compared implementation against project steering, ADRs, business contracts, implementation plans and `phase-status.md`.
- Reviewed architecture, correctness, security, tests, operability and documentation drift.
- Independently re-opened and verified every merged high-severity finding.
- Excluded generated output such as `bin/`, `obj/`, `.next/`, `node_modules/` and lock/generated screenshots.

Specialist outputs are preserved under [`reviewers/`](reviewers/), and the normalized finding set is in [`findings.json`](findings.json).

## Finding summary

| Severity | Count |
| --- | ---: |
| Blocker | 0 |
| High | 7 |
| Medium | 8 |
| Low | 1 |

## High-severity findings

### F-001 — The current solution does not pass its build gate

The canonical build fails on analyzer errors at `Notifications.cs:105` and `LiveEvents.cs:109`. Until this is green, downstream build/test evidence is not reliable for the current revision.

### F-002 — OutcomeUnknown ends the workflow before reconciliation

[`IncidentWorkflow.cs`](../../../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/IncidentWorkflow.cs) escalates and returns when Executor reports `OutcomeUnknown`, even though the domain and Executor explicitly allow a later conclusive reconciliation report. A successfully reconciled action can therefore miss recovery verification entirely.

### F-003 — Revocation can be acknowledged after execution starts

[`ApprovalService.cs`](../../../../services/Sentinel.Api/src/Sentinel.Api.Application/Approvals/ApprovalService.cs) finalizes revocation using a lagging read model before Runtime accepts it. Runtime can reject it after `ExecutionStarted`, while Api has already returned success and permanently reports `Revoked`.

### F-004 — Executor administration endpoints trust unauthenticated callers

[`Program.cs`](../../../../services/Sentinel.Executor/src/Sentinel.Executor.Host/Program.cs) exposes policy and safety-control mutations without authenticating the caller or deriving the actor from a trusted identity. These routes must remain isolated until owning-service authorization exists.

### F-005 — Runtime trusts unsigned approval-state messages

[`RemediationControlService.cs`](../../../../services/Sentinel.Runtime/src/Sentinel.Runtime.Application/RemediationControlService.cs) turns approval, rejection and revocation bus payloads directly into domain commands. Because all services share the messaging resource, the producer boundary is not enforced.

### F-006 — Invalid execution commands can poison valid idempotency keys

[`ExecutorServices.cs`](../../../../services/Sentinel.Executor/src/Sentinel.Executor.Application/ExecutorServices.cs) persists a request under a predictable idempotency key before validating it. An invalid first command can permanently suppress the later legitimate command.

### F-007 — Internal query and replay APIs lack caller and tenant enforcement

Runtime, AgentWorker, Executor and Ingestion expose internal data/replay routes without service authentication, and key Runtime storage reads are not tenant-scoped. Api filtering is currently the only tenant guard. This is consistent with service authentication being deferred to Phase 09, but it is an immediate deployment constraint for the newer endpoints.

## Medium- and low-severity findings

- **F-008:** Executor control mutations and Api audit writes are not atomic, so accepted safety changes can lack an audit record.
- **F-009:** concurrent policy publications can both pass expected-version validation.
- **F-010:** incomplete baseline telemetry can incorrectly produce `CompensationRequired`.
- **F-011:** investigation redelivery can repeat model/provider work and cost.
- **F-012:** Executor never retires cached approval verification keys.
- **F-013:** Runtime's start-confirmation route does not authenticate Executor.
- **F-014:** Temporal tests omit the `OutcomeUnknown` then reconciled-result path.
- **F-015:** `phase-status.md` no longer reflects delivered Phase 05–08 capability.
- **F-016 (low):** architecture tests do not inspect project-reference boundaries.

## Verification results

| Check | Result |
| --- | --- |
| Canonical build | **Failed** — IDE0047 in `Notifications.cs:105`; CA1826 in `LiveEvents.cs:109`; six additional warnings were observed, including file-lock contention during the earlier concurrent run. |
| Canonical lint | **Incomplete** — .NET whitespace verification completed; frontend ESLint made no progress for several minutes and was interrupted; typecheck was not reached. |
| Full canonical tests | **Not run as acceptance evidence** because the solution build is failing. Existing tests were inspected for relevant coverage. |
| JSON review artifacts | Parsed successfully after generation. |
| Source modifications | None made by this review; only files under this report directory were added. |

## What is strong

- The fixed five-service shape and package/service separation remain coherent.
- Runtime remains the incident truth owner and Temporal orchestration is separated from domain state.
- Remediation includes approval attestations, fencing, start confirmation, durable execution state and reconciliation primitives.
- Replay, incident memory, postmortem provenance, scenario packages and evaluation harness work are materially implemented.
- The platform has real health/readiness, OpenTelemetry, HTTP resilience, provider timeouts, inbox/outbox, DLQ and bulkhead foundations.
- Tests are broad in kind: unit, architecture, integration, Orleans, Temporal, Aspire end-to-end, frontend and evaluation. The issue is the newest safety/concurrency paths and the currently red build—not an absence of engineering discipline.

## Phase assessment

- **Phases 00–05:** the ledger's conservative `In progress` status remains appropriate; several stated exit criteria are still open.
- **Phase 06:** implementation is well beyond a skeleton, but F-002, F-003, F-004–F-006, F-008–F-013 and the missing regression coverage prevent calling the safety slice complete.
- **Phase 07:** memory, replay, scenario, postmortem and evaluation capabilities are now real; evidence, retention and end-to-end validation should be reconciled with the phase plan before changing status.
- **Phase 08:** live/control-plane/postmortem work is materially present, but identity, tenant enforcement and authorization remain incomplete.
- **Phase 09:** correctly remains not started. Existing health and resilience primitives are a foundation, not production-hardening completion.

## Recommended repair order

1. Restore a green build and repeat build/lint/test from a clean process state.
2. Fix the remediation lifecycle: unknown-outcome reconciliation and authoritative revocation.
3. Secure the command/control plane: authenticated producers, Executor admin ownership, idempotency reservation and start confirmation.
4. Add tenant-scoped internal contracts and storage predicates; constrain deployment until service authentication exists.
5. Make audit and policy publication transactional/concurrency-safe; fix recovery-baseline validation and investigation retry idempotency.
6. Add regression tests for every high finding, then update the phase ledger based on evidence rather than code presence alone.

## Review limitations

The test, operability and documentation specialist agents hit an account usage limit during fan-out. Those dimensions were completed directly in the primary review and their outputs are included. No live deployment, external Meta endpoint, or production infrastructure was exercised. The full automated suite was not claimed because the build gate is red.

## Repair status, 2026-10-29 evening

Recorded by the session that owns Executor, Gateway and Sentinel.Contracts after the review; the Runtime, AgentWorker and end-to-end work belongs to another session and is marked accordingly.

| Finding | Status | Where |
| --- | --- | --- |
| F-001 build gate | Fixed before this pass | The phase ledger records `build` and `lint` passing on 2026-10-29. |
| F-002 OutcomeUnknown ends the workflow | Fixed by the Runtime session | `IncidentWorkflow.AwaitReconciliationAsync`, `WorkflowOptions.ReconciliationTimeout`, two workflow tests. |
| F-003 revocation acknowledged before Runtime | Gateway side fixed; Runtime endpoint pending | `RevokeApprovalCommandHandler` asks `IRuntimeRemediation` (`POST /v1/remediation/revocations`) and records only on acceptance; refusal is `409`, unreachable is `503`. Runtime must serve the endpoint and verify the `revoke-approval` attestation. |
| F-004 Executor admin trusts callers | Fixed | Gateway signs `OperatorAttestationClaims` per mutation (`OperatorAttestations`); Executor's `OperatorAttestationFilter` verifies scope, resource, content hash, expiry and single use, and the controller records the actor from the claims. |
| F-005 Runtime trusts unsigned decisions | Signing side fixed; Runtime verification pending | Rejections and revocations now carry Gateway attestations with audience `sentinel-runtime`; approvals already did. Runtime must verify all three against the published keys before recording. |
| F-006 idempotency key poisoning | Fixed | `ExecuteRemediationCommandHandler` authenticates before receiving; an unauthenticated command is recorded under an `#unverified:` key and never reported. Regression test added. |
| F-007 internal APIs lack caller and tenant enforcement | Open | Needs the service-to-service authentication decision (no ADR yet) and tenant predicates in Runtime storage. |

Tests run for this pass: Sentinel.Contracts.Tests 34 passed; Sentinel.Executor.Tests 54 passed; Sentinel.Executor.Architecture.Tests 20 passed; Sentinel.Gateway.Tests 66 passed; Sentinel.Gateway.Architecture.Tests 20 passed. The end-to-end suite was not run by this session.
