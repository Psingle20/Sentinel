# Schema-change record template

Copy this section into the change description, or into a dated file under this directory for a breaking change.

## Change

- Message or contract: `<message type or endpoint>`
- Current schema version: `N`
- Proposed schema version: `N` (additive) or `N+1` (breaking)
- Date: YYYY-MM-DD

## What changes

Fields added, deprecated or, for a breaking change, removed or retyped.

## Compatibility

- Additive changes: confirm that the new fields are optional and that existing consumers ignore them.
- Breaking changes: list every consumer, the order in which they accept the new version, and when the producer switches. Consumers accept both versions during the transition (ADR-012).

## Verification

- Snapshot regenerated with `SENTINEL_UPDATE_SNAPSHOTS=1` and reviewed.
- Round-trip tests pass for the old and new versions.
- Replay of stored history is unaffected, or an upcaster is added (Phase 07).
