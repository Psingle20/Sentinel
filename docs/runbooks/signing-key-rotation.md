# Signing key rotation

The Gateway signs every approval, and every operator control change such as the emergency stop, with an ES256 key; Executor and Runtime trust an attestation only if it verifies against a key the Gateway publishes ([ADR-006](../decisions/ADR-006-remediation-control-path.md)). Locally that key is generated in the Gateway's memory and never written anywhere; only its public half is stored and published ([LocalApprovalSigner](../../services/Sentinel.Gateway/src/Sentinel.Gateway.Infrastructure/Security/LocalApprovalSigner.cs)). Production replaces it with a managed, non-exportable key, and this runbook gains that facility's procedure when the deployment definition exists ([Phase 09](../implementation/phase-09-production-hardening.md)).

Use this runbook to check that rotation works, to rotate now, or to respond to a suspected key compromise.

## How rotation works

- The Gateway starts a new key after `Gateway:Signing:RotateAfter`, 12 hours by default, the next time it signs or publishes its key set. A restart is also a rotation, because the key lived only in memory.
- The key it replaces, including one left behind by a restart, stays published until every attestation it signed has expired: `Gateway:Approvals:AttestationLifetime`, 15 minutes by default, plus 5 minutes.
- Executor and Runtime re-read the key set every 5 minutes, and at most every 10 seconds when an attestation names a key they do not know, so a new key is trusted within seconds and a retired key stops verifying within 5 minutes of leaving the set. If the set cannot be read for an hour, they trust no key at all ([ApiApprovalKeys](../../services/Sentinel.Executor/src/Sentinel.Executor.Infrastructure/Clients/ApiApprovalKeys.cs)).
- The local signer runs as a single Gateway instance: its first key retires every key that is still current.

## Check the published keys

The key set is public keys only and needs no identity:

```text
GET $GATEWAY/.well-known/sentinel/approval-keys
```

Each key has its `kid`, `nbf` and, once it is retiring, `exp`, as Unix seconds. Normally there is one key without `exp`, plus for up to 20 minutes after a rotation the key it replaced. The Gateway logs `Approval signing key <kid> is active` at each rotation, and an attestation's header names the `kid` that signed it.

## Rotate now

Restart `gateway-service` from the Aspire dashboard, then read the key set: the read starts the new key. Approvals signed before the restart still verify until they expire, so nothing in flight is lost.

To change the interval, set `Gateway:Signing:RotateAfter` in the Gateway's configuration, for example `Gateway__Signing__RotateAfter=06:00:00` in its environment, and restart it. It is not part of the control-plane configuration, so it cannot be changed through the API.

## A key may be compromised

Whoever holds the key can approve any plan and can release the emergency stop, so the emergency stop alone is not enough here.

1. Stop `executor-service` from the Aspire dashboard. With Executor down nothing executes, whatever is signed ([emergency stop](emergency-stop.md#if-the-gateway-is-unavailable)).
2. Note the suspect `kid` from the key set, then restart `gateway-service` and read the key set again. It now lists a new key and the suspect key with an `exp` 20 minutes away.
3. Wait until the suspect key has left the set. Until then an attestation it signs still verifies.
4. Review what the suspect key could have signed. Every control change in Executor's history, `executor.control_changes`, should match a request in the Gateway's audit, `GET $GATEWAY/api/v1/admin/audit`. Every `plan.approved` entry on an incident's timeline should match a row in the Gateway's `api.approvals` table: an approval the Gateway did not record was signed outside it.
5. Start `executor-service`. It reads a fresh key set on its first verification, so it never trusts the suspect key. If a control change in step 4 was not yours, engage the [emergency stop](emergency-stop.md) again first and review the open incidents before releasing it.

## Verify

1. The key set lists exactly one key without `exp`, and its `kid` matches the latest `is active` log line.
2. An approval made now executes, and its row in `api.approvals` records the new `kid` in its attestation.
3. After a compromise, no key the set listed before step 2 is still listed.

## Tests

[LocalApprovalSignerTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/LocalApprovalSignerTests.cs) cover rotation and a restart, each keeping the previous key published only until its attestations expire; [ApiApprovalKeysTests](../../services/Sentinel.Executor/tests/Sentinel.Executor.Tests/ApiApprovalKeysTests.cs) cover the verifier's refresh, validity windows and staleness limit.
