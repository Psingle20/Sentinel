# ADR-019: Incident memory, replay and retention

- Status: Accepted
- Date: 2026-10-28

## Context

Phase 07 makes incidents reproducible, comparable and useful to later investigations. That needs four things the earlier phases did not decide: where remembered incidents live and who may read them, how a replay avoids live providers and side effects, how much a replay may claim, and how stored incident data is kept, held and deleted. Without a decision, memory could leak across tenants, a replay could quietly call a provider or a model and present the answer as reproduced, and deletion would have no defined reach.

## Constraints

- The incident event stream is the only authoritative incident state ([ADR-004](ADR-004-runtime-state-ownership.md)); memory, projections and postmortems are derived from it.
- Services keep their own data; AgentWorker owns agent executions and Runtime owns incident state ([ADR-007](ADR-007-cross-service-code-and-provider-access.md), [ADR-009](ADR-009-persistence-baseline.md)).
- Telemetry and retrieved text are untrusted; a remembered incident is not evidence for a new one.
- Schema changes are additive: tables are created if missing and never altered or dropped by the services.
- Retention purges are operator actions, never scheduled jobs.

## Considered options

1. A separate memory or replay service with its own store.
2. Memory in Runtime beside the stream it is derived from; captured inputs and investigation replay in AgentWorker beside the executions they belong to.
3. Memory as a vector index of free-text summaries.

## Decision

Adopt option 2.

- **Incident memory.** Runtime writes one memory record per incident, in the same transaction as the events, whenever the incident is resolved or escalated. The record links symptoms, affected services, changes, hypothesis keys, evidence and observation IDs, the decision, the action and the outcome, and carries the run's version manifest and retention metadata. Retrieval matches a feature index (`service:`, `symptom:`, `change-service:`, `change-kind:`, and for closed incidents `cause:`, `culprit:`, `action:`, `outcome:`) with weighted Jaccard similarity. Retrieval is always scoped to one tenant, to one environment unless `Runtime:Memory:CrossEnvironment` is set, to retained or legally held records, and never returns privacy classes listed in `ExcludedFromRetrieval`.
- **Hints, not evidence.** Similar incidents travel to investigators inside the investigation request event, so they are part of the stream and replay identically. Prompt `investigator-v3` presents them as hints that must be confirmed with tools and never cited.
- **Replay levels.** Every result names its level, and claims determinism only when every step was deterministic:
  1. *Domain replay* (Runtime) rebuilds state and the projection from the stream, compares them with what is stored, and re-runs each recorded decision on the stream prefix it was made from with the settings it recorded. It reads; it writes, sends and signals nothing.
  2. *Investigation replay* (AgentWorker) re-runs every recorded tool call over the execution's captured provider responses and compares the results byte for byte. The investigator itself is rerun only when it is deterministic code or a deterministic model.
  3. *Model reevaluation* (AgentWorker) reruns a selected model profile and prompt version against the frozen inputs. It is reproducible only if the model is.
  4. *System simulation* reruns the failure against the reference system through the scenario runner.
- **Captured inputs.** AgentWorker records the command and every provider response an execution received. During a replay, queries are served only from that record, matched by the canonical hash of the request; a query outside it fails explicitly and is counted, and no live provider is ever called.
- **Version manifest.** Run records carry the decision algorithm, every model and prompt version that reported, the tool catalog version, the policy version, the redaction policy version and the dataset.
- **Retention and deletion.** Memory records carry `retainUntil` (`Runtime:Memory:Retention`, 400 days by default), a legal hold, a privacy class and the redaction policy version. An operator can place a legal hold, delete one incident's memory for privacy, or run the purge, which is a dry run unless explicitly asked otherwise and only removes records past retention and not on hold. Deletion replaces the record with a tombstone and removes its features from the index in the same transaction, and a later event of the stream never brings it back.
- **Event upcasting.** A stored event written by an older shape is completed by a pure upcaster before deserialization; the stream itself is never rewritten.

## Consequences

### Positive

- Memory and replay sit beside the data they derive from, with no new deployable and no cross-service reads.
- Investigation inputs, including memory hints, are reproducible from what was recorded, and every replay result states how far it can be trusted.
- Deletion has a defined reach that tests can verify: the record, the derived index and retrieval.

### Negative

- Captured provider responses make execution records much larger; their retention follows the executions'.
- Privacy deletion covers incident memory and its index, not the event stream or the projections. Erasing an incident's stream needs a crypto-shredding design that does not exist yet.
- Feature similarity finds incidents that look alike structurally; it cannot match on meaning the features do not capture.

## Follow-up work

- Erasure of event streams and projections, with crypto-shredding of payload fields (Phase 09).
- Operator authorization for `/admin/memory/*` (Phase 09).
- Retention for agent executions and captured inputs (Phase 09).
- Semantic retrieval over memory, only if feature retrieval measurably misses relevant incidents (Phase 10).

## Revisit criteria

Replay or retrieval load that measurably affects incident handling, which would justify the optional replay worker; or evaluation showing feature similarity misses incidents that responders consider relevant.
