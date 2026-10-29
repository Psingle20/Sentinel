# Emergency stop

Use this when Sentinel must not execute any remediation: a suspected bad plan, an unexpected action, a change freeze, or any doubt about the control path. The stop is held by Executor in its own database, so it survives restarts and needs no other service ([ADR-006](../decisions/ADR-006-remediation-control-path.md)). It blocks execution only; investigation, diagnosis and planning continue, and responders keep seeing incidents.

## Engage

An operator engages the stop through the Gateway, which signs an operator attestation for Executor:

```text
POST $GATEWAY/api/v1/admin/controls/emergency-stop
X-Sentinel-User: operator@example.test
Idempotency-Key: <fresh value>
Content-Type: application/json

{ "enabled": true }
```

To stop one kind of action instead, for example rollbacks, use `POST $GATEWAY/api/v1/admin/controls/actions/rollback` with the same body.

## Verify

1. `GET $GATEWAY/api/v1/admin/controls` reports `emergencyStop: true`.
2. Executor recorded the change with your identity: its control history in the `executor.control_changes` table lists the change, newest first, with `changed_by` set to the operator from the attestation.
3. The Gateway's audit, `GET $GATEWAY/api/v1/admin/audit`, lists the request.
4. A plan approved now is rejected at execution with the emergency stop as the reason, and nothing reaches the remediation target.

## Release

Send the same request with `{ "enabled": false }` and verify that `GET .../controls` reports `emergencyStop: false`. Plans rejected while the stop was engaged are not retried; a responder approves a new plan if the action is still wanted.

## If the Gateway is unavailable

The stop is Executor's control, but Executor accepts it only with a Gateway-signed operator attestation, so there is no supported path around the Gateway. If the Gateway cannot be reached and an action must be prevented, stop Executor itself from the Aspire dashboard: with Executor down nothing executes, and requests already received are reconciled when it starts again ([ExecutionService](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/Services/ExecutionService.cs)).
