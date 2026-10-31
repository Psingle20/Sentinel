# Phase E06 – Incident artifacts

Enhancement phase. Design source: [Model roles beyond the specialists](../architecture/model-roles.md), role 2, under [ADR-024](../decisions/ADR-024-model-roles.md) (accepted 2026-10-31). This document is the delivery view; the design holds the artifact lifecycle, the `ArtifactFinding` shape and the rules.

## Outcome

A responder can attach a screenshot, diagram or document to an incident, and Sentinel reads it as an untrusted evidence candidate: a multimodal specialist extracts typed claims, Ingestion corroborates each claim through its providers, a confirmed claim becomes evidence citing the observations found, and an unconfirmed one stays visible as a claim with no weight. Nothing in an artifact can become evidence, a hypothesis or an action by itself.

## Primary service ownership

- `Sentinel.Gateway` owns attachment: the authenticated, tenant-scoped upload with type, size and count limits, and the artifact's place in the incident's external contract.
- `Sentinel.Runtime` owns the artifact's record on the incident, the `ArtifactAttached` event, retention with the incident, and the acceptance rule that only a corroborated claim becomes evidence.
- `Sentinel.AgentWorker` owns the `artifacts` role: reading the artifact as a tool result, the `ArtifactFinding` contract validation, the corroboration queries through the existing observation tools, and the planted-instruction scan over extracted text.
- `Sentinel.Ingestion` owns redaction of text inside images and documents, extending its policy; it adds no provider.
- `Sentinel.Contracts` gains the artifact reference, `ArtifactAttached` and `ArtifactFinding`.
- `Sentinel.Web` gains the attach control and the artifact and claim views on the incident page and in Judge mode.
- `Sentinel.Executor` changes nothing.

## Entry evidence required

- Phase E05's escalation role has shown how a new role enters the pipeline with the test double, the harness group and replay, so this phase follows a proven path.
- The planted-instruction scan runs over tool results ([Phase E03](phase-e03-demonstration-depth.md)).
- Redaction runs in Ingestion before storage with tests ([Phase 02](phase-02-telemetry-ingestion.md)).
- Judge mode's first slice exists, so claims and corroboration have a place to be shown ([Phase E04](phase-e04-judge-mode.md)).

## Slices

| Slice | Work | Evidence when done |
| --- | --- | --- |
| 1 – Attach and store | Gateway upload with limits; `ArtifactAttached` on the incident; redaction of extracted text; retention with the incident; the incident page lists artifacts as untrusted | Contract and integration tests; a redaction test with a secret in an image's text; tenant-scope and limit tests |
| 2 – The role with the test double | The `artifacts` role reading screenshots; `ArtifactFinding` validation; corroboration through existing tools; the acceptance rule in Runtime; the test double produces a finding for a fixture screenshot | A fixture screenshot of a growing dead-letter queue is corroborated against the queue metrics in `S6`, and a doctored one is refuted and shown as unconfirmed |
| 3 – Live profile and evaluation | The role on a multimodal Muse Spark profile; documents beside screenshots; the harness `artifacts` group and the corroboration-rate measure; one recorded live run | Report with corroboration rate; no uncorroborated claim reaches evidence in any scenario |

## In scope

- Screenshots first, documents second; both bounded in size and count per incident.
- A scenario package that attaches a fixture artifact to `S6` and to one deployment scenario, with ground truth saying which claims are true.
- The incident page and Judge mode showing artifacts, their claims and each claim's corroboration state.

## Primary deliverables

1. Artifact attachment through the Gateway and its record on the incident.
2. Redaction of text in images and documents.
3. The `artifacts` role, `ArtifactFinding` and corroboration, with the acceptance rule.
4. The artifact scenario packages, the harness group and the corroboration-rate measure, and a recorded live run.

## Test plan

- Gateway: limits, types, tenant scope and authentication on upload; an artifact never attaches to another tenant's incident.
- Ingestion: redaction of text in images and documents, including a secret and a planted instruction.
- AgentWorker: a finding without a corroboration query is rejected; a claim is never cited as evidence directly; the planted-instruction scan flags an artifact that addresses "AI agents"; the execution is replayable from the artifact hash and the stored provider response.
- Runtime: only a corroborated claim becomes evidence; an uncorroborated claim carries no weight in consensus.
- End to end: the corroborated and the refuted fixture artifacts in `S6` with the test double.
- Evaluation: corroboration rate reported; the gate still passes.

## Exit criteria

- A screenshot attached to an incident yields claims that are confirmed or refuted through Ingestion, with the result shown on the incident and in Judge mode.
- No uncorroborated claim becomes evidence, supports a hypothesis or justifies an action in any scenario.
- Redaction covers text inside images and documents, with tests.
- The role is replayable without the provider, and its live run is recorded with cost.
- The ledger records the scenario runs and the harness report.

## Deferred deliberately

- Video and audio artifacts.
- Artifacts as a source of topology or service-catalog changes.
- Automatic attachment from chat tools or ticketing systems.
