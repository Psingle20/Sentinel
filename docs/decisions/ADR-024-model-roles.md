# ADR-024: Model roles beyond the specialists

- Status: Proposed
- Date: 2026-10-31

## Context

Sentinel uses Muse Spark as six specialist investigators behind `IModelProvider`, with a deterministic verifier and consensus deciding. The `meta-wonder` profile exists without a role of its own. Three further uses of models have been proposed, each adding a kind of evidence or reasoning the product lacks: an escalation investigator for ambiguous cycles, operator-supplied artifacts as evidence candidates, and a visual probe of the user-facing application. Two further proposals, generated imagery for postmortems and communications and a model-assisted approval, would weaken the product's central claim that every conclusion is verifiable and every action human-controlled. Without a decision, model use grows by vendor catalogue rather than by responsibility, and the safety rules get restated per feature or forgotten.

## Constraints

- A model proposes; it never decides, approves, acts or changes policy ([ADR-006](ADR-006-remediation-control-path.md)).
- Model access stays behind Sentinel-owned interfaces and typed, validated contracts; no Meta type reaches the domain ([ADR-015](ADR-015-model-abstraction.md)).
- Every model input is untrusted data; findings cite only observations a tool returned; executions are replayable without the provider ([ADR-019](ADR-019-incident-memory-replay-and-retention.md)).
- Provider adapters live in the owning service and new observation kinds are published under the scope rules of [ADR-018](ADR-018-observation-publication-scope.md).
- The five-service topology holds; a new deployable needs its own decision ([ADR-001](ADR-001-production-first-service-topology.md)).
- Spend caps per profile, per day and in total apply to every role.

## Considered options

1. **Add model uses by catalogue.** Integrate each available Meta capability where it fits and let the pipeline absorb the output. Rejected: it multiplies untrusted inputs faster than the rules that govern them, and generated imagery or voice-driven approval would reach the record.
2. **Add no further model use.** Keep the six specialists and the deterministic pipeline. Rejected: ambiguous cycles end in escalation that one more observation would have resolved, and incidents that begin with a screenshot or a customer-visible failure stay invisible to Sentinel.
3. **Give each model use one responsibility inside the existing pipeline, and rule out the uses that would weaken it.** Chosen.

## Decision

Adopt option 3 as described in the [model roles design](../architecture/model-roles.md), with these rules:

- **Escalation investigator.** Once per cycle, only when consensus would abstain for ambiguity, the `meta-wonder` profile receives structured hypothesis state and returns one `DiscriminatingQuery` naming an existing read-only tool call and the expected result under each hypothesis. AgentWorker runs it, the verifier and consensus run again, and the role never proposes a diagnosis, widens a range or runs twice. Phase E05.
- **Artifact investigator.** Operator-supplied screenshots, diagrams and documents attach to an incident through the Gateway, bounded, redacted and labelled untrusted. A multimodal Muse Spark execution produces typed `ArtifactFinding`s, each corroborated through Ingestion's providers before it can become evidence; an uncorroborated claim is shown as a claim and carries no weight. The planted-instruction scan covers extracted text. Phase E06.
- **Visual synthetic monitor.** A recorded browser journey becomes a new canonical `VisualObservation` kind through an Ingestion provider: a segmentation and tracking model locates elements, deterministic rules measure state and time, and every region links to its frame. Runtime detects and correlates on it like any signal. Phase E07, after Judge mode and the two roles above.
- **Ruled out.** No generated imagery appears in an incident record, a postmortem, a notification or a communication derived from an incident; charts, timelines and topology are rendered deterministically from records. No model approves, rejects, revokes, mints identity, overrides policy or issues a capability, whatever the input modality. These two rules are added to the threat model.
- **Not adopted now.** A private or shadow inference path on open-weight models, for lack of access; incident-bridge transcription, for lack of a consent and retention policy. Provider unavailability keeps its current honest behaviour.
- **Evaluation.** Each role is a separate investigator group in the evaluation harness and must be scored against the scenario packages before it is relied on; escalation yield and corroboration rate are added as measures.

## Consequences

### Positive

- Each model does one thing the pipeline cannot do without it, and the thing is checkable: a query, a claim, a measurement.
- The safety rules are stated once for every modality instead of per feature, and the two uses that would erode them are closed in writing.
- The evaluation harness, not a demonstration, decides whether a role stays.

### Negative

- Three new untrusted input kinds (structured state to Wonder, artifacts, frames) widen the redaction and injection-scanning surface and need their own tests.
- Artifacts and frames add storage, retention and replay weight that telemetry did not have.
- The escalation role adds a model call and a tool call to ambiguous cycles, lengthening them by design.

## Follow-up work

- Phase E05: the escalation role, its trigger in consensus, the test double's behaviour, the harness group and the escalation-yield measure.
- Phase E06: artifact attachment through the Gateway, redaction of image and document text, the artifact investigator, corroboration, and the corroboration-rate measure.
- Phase E07: the `VisualObservation` kind, the Ingestion provider, the reference checkout journey and its fixtures, the detection rule and the scenario.
- Threat model: the two ruled-out uses and the three new input kinds.
- Change this ADR to Accepted when Phase E05's role is scored by the harness with zero wrong diagnoses introduced on the scenario packages.

## Revisit criteria

Reopen if access to open-weight Meta models makes a private or shadow path possible, if a user asks for incident-bridge transcription with a consent and retention policy in hand, if the escalation role introduces a wrong diagnosis on any scenario, or if artifacts prove too costly to redact reliably.
