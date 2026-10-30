# Phase 06 – Policy-governed remediation and recovery

## Outcome

Convert an evidence-backed diagnosis into a constrained plan, obtain the required approval, execute through the sole privileged broker and determine success from telemetry rather than an API response.

## Primary service ownership

`Sentinel.Executor` owns execution policy enforcement, capabilities, privileged credentials, adapters, action verification and execution audit. `Sentinel.Gateway` captures approval against an immutable plan and sends a signed attestation to `Sentinel.Runtime`, never to Executor. `Sentinel.Runtime` orchestrates the state transition and owns the recovery decision. The control path follows [ADR-006](../decisions/ADR-006-remediation-control-path.md).

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) static policy, single plan template and fixed-window recovery check. The skeleton's attestation, fencing and start-confirmation contracts are kept and hardened. The end-to-end suite must pass with policy-as-code and full recovery verification on the path.

## In scope

- Typed remediation plan with target, preconditions, expected effect, risk, blast radius, rollback/compensation and verification criteria.
- Policy-as-code evaluation by tenant, environment, action class, confidence, severity, blast radius and time window. Executor is the only policy engine: advisory plan-time evaluation for Runtime, and authoritative execution-time enforcement.
- Modes: read-only, suggestion-only, approval-required and constrained autonomous.
- Capability-based authorization with action, resource, scope, limits, expiry and single-use/idempotency semantics.
- Human approval, rejection, expiration and revocation workflow, using single-use Api-signed JWS attestations bound to plan ID, version and content hash, with the claims listed in ADR-006.
- Production signing key held non-exportable in a managed signing facility behind a Sentinel-owned signing interface.
- Execution broker as the only component allowed to resolve privileged credentials and invoke action adapters.
- One reference rollback or equivalent remediation adapter for the golden scenario, and the `order-service` rollback needed by `S3`.
- Fencing with the incident stream version ([ADR-004](../decisions/ADR-004-runtime-state-ownership.md)) plus a start confirmation from Runtime before any side effect.
- Durable execution state machine in Executor, from `Received` through `ActionVerified`, `Failed` or `OutcomeUnknown`, with rejection, abort, reconciliation and compensation paths (ADR-006).
- Adapter contract of `Execute`, `Inspect`, `VerifyAction` and `Compensate`. `OutcomeUnknown` is reconciled through `Inspect`, never by repeating the action.
- Saga and compensation behavior for partially completed remediation.
- Action verification in Executor, confirming the action took effect at the target.
- Recovery verification in Runtime, comparing pre-action baseline, expected signals and post-action observations. Recovery is inconclusive unless the observation query's `ObservedThrough` covers the full window and the result is complete and untruncated ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)).
- Outcomes: recovered, unchanged, degraded, inconclusive or compensation required.
- Automatic incident reopen or escalation when recovery is not demonstrated.

## Trust boundary

```text
Model/agent proposal
  -> schema validation
  -> deterministic policy evaluation (advisory, plan time)
  -> human approval when required (Api-signed attestation via Runtime)
  -> execution-time policy enforcement, attestation and fencing checks
  -> start confirmation from Runtime
  -> scoped capability
  -> execution broker
  -> action adapter and action verification
  -> telemetry-based recovery verification in Runtime
```

No model, specialist agent, workflow prompt or retrieved document crosses directly into privileged execution.

## Primary deliverables

1. Remediation plan and action contract.
2. Policy engine with explainable allow, deny and approval-required results.
3. Approval API, attestation signing with key rotation, and the approval state machine.
4. Capability issuer/validator and audit events.
5. Execution broker with credential isolation, fencing, start confirmation, idempotency and the durable execution state machine.
6. Reference rollback adapters implementing `Execute`, `Inspect`, `VerifyAction` and `Compensate`.
7. Runtime recovery-verification workflow and incident reopen behavior.

## Safety and audit requirements

- Every evaluation records policy version, inputs, rule trace and result.
- Every approval records actor, time, plan version and capability scope.
- Every execution records request, adapter response, timing and idempotency key.
- Approval of one plan version cannot authorize a later mutated plan.
- Executor never accepts an approval that Api did not sign, and execution fails closed when Runtime cannot confirm the start.
- Recovery uses service health and SLO signals, not execution success alone.
- Emergency stop and action-disable controls are independent of the model path.

## Test plan

- Policy decision tables and boundary tests.
- Approval expiration, revocation and plan-mutation tests.
- Forged, mismatched, expired and replayed attestation tests, including an unknown `KeyId` and a wrong `Audience`.
- Plan superseded between approval and start, and Runtime unavailable at start confirmation.
- Duplicate execution and stale-fencing-token tests.
- Adapter timeout, ambiguous result and partial-success tests.
- Executor crash in every execution state; from `OutcomeUnknown`, reconciliation through `Inspect` reaches the true outcome without repeating the action.
- Recovery verification against late, incomplete and truncated telemetry, which must produce an inconclusive result.
- Compensation failure and manual-escalation tests.
- Recovery success, unchanged, regression and inconclusive tests.
- End-to-end golden scenario from diagnosis through verified rollback.

## Exit criteria

- The golden rollback cannot execute without the required policy result and approval.
- A plan superseded after approval cannot start.
- Agents and workflows cannot access production credentials directly.
- Retrying an execution request cannot repeat the logical action, including after an Executor crash with an unknown outcome.
- An attestation cannot be used twice.
- Recovery is never declared from stale or incomplete telemetry.
- Successful adapter response without healthy telemetry does not close the incident.
- Failed verification reopens or escalates the incident with full history preserved.

## Deferred deliberately

Only the reference action is enabled. Broader remediation coverage and high-risk autonomous operation require production evidence and Phase 10 governance.

