# Dead letters

A message whose handling keeps failing moves to a dead-letter stream after its delivery limit, so it stops blocking the stream behind it ([ADR-008](../decisions/ADR-008-event-transport.md)). Use this runbook when a dead-letter count rises or an expected step never happened.

The operator endpoints exist in Development, or where `Sentinel:OperatorEndpoints:Enabled` is set ([OperatorEndpoints](../../packages/Sentinel.ServiceDefaults/src/Sentinel.ServiceDefaults/OperatorEndpoints.cs)). They are unauthenticated and must not be exposed beyond a trusted network until service authentication exists ([threat model](../architecture/threat-model.md#open-risks)).

## Find the dead letters

Ask the service that consumes the stream. The streams are named in [SentinelStreams](../../packages/Sentinel.Contracts/src/Sentinel.Contracts/Messaging/SentinelStreams.cs), for example `sentinel.runtime.commands` for approvals relayed to Runtime:

```text
GET $RUNTIME/admin/dead-letters?stream=sentinel.runtime.commands&count=50
```

Each entry carries the original envelope and the reason it was dead-lettered, such as "Failed after 5 deliveries: ...".

## Decide

- **The cause is fixed**, for example a dependency was down: replay the entry.
- **The message is invalid**, for example a decision whose attestation does not verify: do not replay it. It would fail the same way; leave it for the audit.
- **The cause is unknown**: read the consuming service's logs around the entry's last delivery before replaying anything.

## Replay

```text
POST $RUNTIME/admin/dead-letters/replay
Content-Type: application/json

{ "stream": "sentinel.runtime.commands", "entryId": "<entry id from the listing>" }
```

Replay is safe to repeat: consumers record each message ID in their inbox and skip a duplicate ([ADR-012](../decisions/ADR-012-event-semantics.md)). Verify that the entry no longer appears in the listing and that the step it carried happened, for example that the incident's timeline shows the approval.
