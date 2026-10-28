# ADR-006: Remediation control path

- Status: Accepted
- Date: 2026-10-27

## Context

The production topology sent an approved plan from `Sentinel.Api` directly to `Sentinel.Executor`. `Sentinel.Runtime` owns the workflow that waits for approval but was not on that path. Runtime could therefore supersede the decision, close the incident or time out the workflow while Executor still ran the previously approved plan, and Runtime would learn about the action only afterwards.

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

- `Sentinel.Api` never sends commands to `Sentinel.Executor`.
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
