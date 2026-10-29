# Projection rebuild

Runtime's incident event stream is the truth; the incident views the Gateway and the web app read are projections of it that can be rebuilt at any time ([ADR-004](../decisions/ADR-004-runtime-state-ownership.md)). Use this runbook when an incident view disagrees with its timeline, after restoring a database, or when a projection table was lost.

## Check first

A domain replay compares one incident's projection with its stream without changing anything:

```text
POST $GATEWAY/api/v1/incidents/<incident id>/replays/domain
X-Sentinel-User: responder@example.test
Idempotency-Key: <fresh value>
```

`projectionMatches: false`, with `projectionDifferences`, confirms the drift. `stateStable` and each decision's `matches` should be true; if a decision does not replay, keep the incident for investigation rather than rebuilding over it.

## Rebuild

```text
POST $RUNTIME/admin/projections/rebuild
```

Runtime replays every stream into fresh projections. Runtime's operator endpoints are unauthenticated until Phase 09 ([ADR-017](../decisions/ADR-017-runtime-actor-and-workflow-hosting.md)), so call this only from a trusted network.

## Verify

1. The domain replay above now reports `projectionMatches: true`.
2. `GET $GATEWAY/api/v1/incidents` lists the same incidents as before, with current statuses.
3. A rebuild produces the same projections the stream produced the first time ([RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs)), so running it again is safe.
