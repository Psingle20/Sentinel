# ADR-006: Remediation control path

- Status: Accepted; service names follow the ADR-020 rename (`Sentinel.Api` is `Sentinel.Gateway`)
- Date: 2026-10-27

## Context

The production topology sent an approved plan from `Sentinel.Gateway` directly to `Sentinel.Executor`. `Sentinel.Runtime` owns the workflow that waits for approval but was not on that path. Runtime could therefore supersede the decision, close the incident or time out the workflow while Executor still ran the previously approved plan, and Runtime would learn about the action only afterwards.

Two related ownership questions were also unresolved:

- The README gave Executor "recovery checks", while Phase 06 and the first-slice description gave Runtime the recovery decision.
- Runtime must know whether a plan needs approval before requesting it, but the policy engine lives in Executor.

## Constraints

- Models and agents never reach privileged execution ([ADR-001](ADR-001-production-first-service-topology.md)).
- Approval binds to one immutable plan version (Phase 06).
- `Sentinel.Runtime` is authoritative for incident state, and its stream version is the fencing token ([ADR-004](ADR-004-runtime-state-ownership.md)).
- Execution fails closed: when a check cannot be made, the action does not run.

## Considered options

1. Api → Executor directly (the earlier draft).
2. Api → Runtime → Executor, with Executor trusting Runtime's word that a plan is approved.
3. Api → Runtime → Executor, with approvals signed by Api and verified by Executor, and a start confirmation from Runtime before any side effect.

## Decision

Adopt option 3. Runtime stays in the control path because it knows whether the incident is still active, the plan is still current, the diagnosis was superseded, the approval expired, execution already started, or recovery is being evaluated.

```text
AgentWorker proposal
  -> Runtime records plan (plan ID, version, content hash)
  -> Runtime asks Executor for a plan-time policy evaluation (advisory)
       allow | deny | approval-required, with policy version and rule trace
  -> Runtime records the evaluation and, if required, requests approval

Web -> Api: approve(plan ID, plan hash)
  -> Api authenticates and authorizes the approver
  -> Api issues a signed approval attestation
  -> Api -> Runtime: record approval
  -> IncidentGrain checks the plan is still current, appends RemediationApproved
     and signals the workflow

Workflow -> Executor: execute(plan, attestation, fencing token, idempotency key)
  -> Executor verifies the attestation and that it has not been used
  -> Executor re-evaluates policy at execution time (authoritative)
  -> Executor rejects a fencing token lower than the highest seen for the incident
  -> Executor -> Runtime: confirm start(plan ID, fencing token)
       Runtime appends RemediationStarted only if the plan is still the
       current approved plan; otherwise Executor aborts with no side effect
  -> Executor issues a scoped single-use capability and runs the adapter
  -> Executor performs action verification
  -> Executor -> Runtime: execution result

Runtime workflow
  -> waits for the plan's observation window
  -> recovery verification from observations
  -> RecoveryVerified | RecoveryFailed | RecoveryInconclusive
```

### Rules

- `Sentinel.Gateway` never sends commands to `Sentinel.Executor`.
- `Sentinel.Runtime` relays approvals but cannot create or alter them.
- `Sentinel.Executor` is the only policy engine. Plan-time evaluation is advisory and recorded. Execution-time enforcement is authoritative; if policy changed in between, the execution-time result wins and may require a new approval.
- The start confirmation records the point of no return in the authoritative stream. If Runtime is unreachable, the action does not start. The added latency is accepted because remediation safety outweighs it.
- Verification is split:
  - **Action verification** (Executor): did the requested action occur at the target, for example the previous revision is serving and the rollout completed? This is part of each adapter's contract.
  - **Recovery verification** (Runtime): did the system actually recover against the plan's expected signals? Runtime owns `RecoveryVerified`, `RecoveryFailed`, `RecoveryInconclusive`, reopen and escalation. A successful adapter response never implies recovery.
- The emergency stop and action-disable controls in Executor remain independent of Runtime, Api and every model path.

### Approval attestation

The attestation is a standard signed token, a JWS using an asymmetric algorithm, not a Sentinel-specific signature scheme.

| Claim | Purpose |
| --- | --- |
| `AttestationId` | Unique ID. Executor records each consumed ID, so an attestation is single-use. |
| `TenantId`, `EnvironmentId`, `IncidentId` | Scope binding |
| `PlanId`, `PlanVersion`, `PlanHash` | Binds the approval to one immutable plan |
| `ApproverId`, `ApprovalScope` | Who approved and what the approval covers |
| `PolicyVersion` | The policy evaluation the approver saw |
| `IssuedAt`, `ExpiresAt` | Short validity window |
| `KeyId`, `Issuer`, `Audience` | Key rotation and intended recipient (`Sentinel.Executor`) |

- In production, the signing key is non-exportable and held by a managed signing facility, such as a cloud KMS or an HSM, behind a Sentinel-owned signing interface so the reference deployment stays cloud-neutral.
- Executor holds only verification keys, resolved by `KeyId`.
- Local development uses a generated key pair from local secret configuration, never a committed key.

### Executor execution state machine

Every execute request is tracked in a durable state machine owned by Executor:

```text
Received
 -> Rejected            invalid or consumed attestation, policy denied, stale fencing token
 -> Reserved            idempotency key, attestation and capability claimed
     -> Aborted         Runtime refused start confirmation
     -> StartConfirmed
         -> Executing
             -> ActionVerified
             -> Failed
             -> OutcomeUnknown
OutcomeUnknown -> Reconciling -> ActionVerified | Failed
Failed         -> Compensating -> Compensated | CompensationFailed (escalate)
```

- Each transition is persisted before the next external call.
- **`OutcomeUnknown` is reconciled, never retried blindly.** If Executor crashes, times out or receives an ambiguous adapter response after `Executing`, it inspects the external target to learn what actually happened before deciding anything else.
- Runtime treats `OutcomeUnknown` and `Reconciling` as "not recovered", not as failed. The action is not retried until reconciliation completes.

### Adapter contract

Every remediation adapter implements:

| Operation | Purpose |
| --- | --- |
| `Execute` | Perform the action, idempotent on the request's idempotency key |
| `Inspect` | Report the target's current state, for example which revision is serving. Used for reconciliation and preconditions. |
| `VerifyAction` | Confirm that the requested action took effect |
| `Compensate` | Undo or neutralize a partially applied action |

### Amendment, 2026-10-29: every decision is signed, revocation is synchronous, operators are attested

The implementation review of 2026-10-29 found three gaps between this decision and the code: Runtime applied rejections and revocations from the shared message bus without any proof of who decided; the Gateway recorded a revocation before Runtime, the authority on the point of no return, had accepted it; and Executor's operator endpoints trusted an actor named in the request body. Executor also bound an incoming command to its predictable idempotency key before checking its attestation, so an unverified command could occupy the key the genuine one would arrive under. The following refinements close them without changing who signs, who relays and who verifies.

- **Every approver decision is a signed attestation.** The Gateway signs approvals as before (scope `execute-plan`, audience `sentinel-executor`), and now also rejections (`reject-plan`) and revocations (`revoke-approval`), both with audience `sentinel-runtime`. The claims bind the decision to the incident's current plan ID, version and content hash and name the approver. `RecordRejection` and `RecordRevocation` carry the token in an optional `Attestation` field. Runtime must verify the signature against the Gateway's published keys and the claims against the incident before recording a rejection or revocation, and must verify the approval's signature and plan binding before recording an approval, so a message on the shared bus proves nothing by itself.
- **Revocation is a bounded request, not a message.** The Gateway calls `POST /v1/remediation/revocations` on Runtime with the `RecordRevocation` body and records the revocation only when Runtime answers `RevocationResponse.Accepted`. A refusal, for example because `ExecutionStarted` was already appended, is returned to the approver as a conflict and the approval stands. An unreachable Runtime also leaves the approval standing. The `runtime.record-revocation` message is retired.
- **Operator actions carry an operator attestation.** For policy publication and control changes the Gateway signs `OperatorAttestationClaims`: the authenticated actor, a scope (`publish-policy` or `set-control`), the resource and a hash of the submitted content, valid for about two minutes and single-use. The token travels in the `X-Sentinel-Operator-Attestation` header. Executor verifies it, consumes it and records the actor from the claims; a body's `Author` or `Actor` field is informational. The same mechanism applies to the other owning services' admin endpoints when they are hardened.
- **An unauthenticated execute command binds nothing.** Executor checks the plan hash and the attestation before it receives a command under its idempotency key. A command that fails that check is recorded for audit under a key of its own, is not reported to Runtime, and leaves the genuine key free. Policy denials and failed preconditions, which can only follow authentication, are reported as before.

Verification keys are the same key set the Gateway already publishes; Runtime resolves them exactly as Executor does. Service-to-service authentication for the remaining internal calls is a separate decision still to be recorded.

## Consequences

### Positive

- Execution always reflects current authoritative incident state; a superseded decision cannot be executed.
- A compromised or buggy Runtime cannot fabricate, alter or replay an approval.
- An Executor crash cannot cause a duplicate or unaccounted action.
- One policy implementation serves both plan-time and execution-time decisions.
- "Recovery" has a single owner and a single meaning.

### Negative

- Approval takes an extra hop, and execution adds a bounded synchronous call from Executor to Runtime.
- Api depends on a managed signing facility in production, including key rotation.
- Every adapter must implement `Inspect`, which some targets make harder than `Execute`.
- Runtime unavailability blocks execution. This is intentional, but it must be visible to operators.

## Follow-up work

- Define the attestation, execute, start-confirmation, execution-state and execution-result contracts in `Sentinel.Contracts`.
- Implement the signing interface with a local development key provider and a managed production provider.
- Add the Phase 06 tests: missing, expired, mismatched, forged and replayed attestations; stale fencing tokens; superseded plans; Runtime unavailable at start confirmation; and an Executor crash in every state, including reconciliation from `OutcomeUnknown`.

## Revisit criteria

Revisit if multi-party approval (Class 3) needs a different attestation model, or if the start confirmation measurably limits remediation latency for an action class that has a documented safety case.
