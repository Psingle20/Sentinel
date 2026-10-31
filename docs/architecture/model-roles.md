# Model roles beyond the specialists

- Status: Accepted through [ADR-024](../decisions/ADR-024-model-roles.md) on 2026-10-31
- Delivery: [Phase E05 – Escalation investigator](../implementation/phase-e05-escalation-investigator.md), [Phase E06 – Incident artifacts](../implementation/phase-e06-incident-artifacts.md), [Phase E07 – Visual synthetic monitor](../implementation/phase-e07-visual-synthetic-monitor.md); the [phase status ledger](../implementation/phase-status.md) records progress
- Related: [architecture §19](architecture.md#19-multi-agent-architecture), [§49](architecture.md#49-prompt-injection-and-tool-safety), [§50](architecture.md#50-meta-native-reference-deployment), [ADR-014](../decisions/ADR-014-provider-abstraction.md), [ADR-015](../decisions/ADR-015-model-abstraction.md), [ADR-018](../decisions/ADR-018-observation-publication-scope.md), [Judge mode](judge-mode.md)

## Purpose

Sentinel uses one model family today in one role: Muse Spark, through the `meta` and `meta-deep` profiles, as the six specialist investigators, with the deterministic verifier and consensus deciding. The `meta-wonder` profile exists but has no role of its own; it is only a suggested routing for the `changes` specialist in demonstrations.

This document gives each further use of a model a distinct responsibility in the evidence pipeline, and refuses the uses that would weaken the pipeline. Three roles are added, each a separate enhancement phase:

1. **An escalation investigator** that proposes the one discriminating observation when consensus cannot separate two explanations.
2. **An artifact investigator** that reads operator-supplied screenshots, diagrams and documents as untrusted evidence that Ingestion must corroborate.
3. **A visual synthetic monitor** that turns a recorded user journey into canonical observations, so an incident the customer sees opens before the backend telemetry shows it.

Two uses are ruled out: generated imagery anywhere a fact lives, and any model in the approval path.

## Invariants every role inherits

The rules that make Sentinel's reasoning verifiable do not bend for a new model, modality or vendor. Each role below is designed inside them, and its tests prove it stays inside them.

- A model proposes; it never decides, approves, acts or changes policy ([ADR-006](../decisions/ADR-006-remediation-control-path.md)).
- Everything a model reads is untrusted data in the data channel, including an image, a transcript or a document; findings validation refuses actions in model output and the planted-instruction scan runs over every input kind ([architecture §49](architecture.md#49-prompt-injection-and-tool-safety)).
- A finding counts only when it cites observations a tool returned in that execution, and every execution records model, prompt version, tool calls, provider responses, tokens and cost so it can be replayed without the provider ([ADR-015](../decisions/ADR-015-model-abstraction.md), [ADR-019](../decisions/ADR-019-incident-memory-replay-and-retention.md)).
- Spend is capped per profile, per day and in total; at a cap the role abstains and says why.
- The verifier and consensus remain deterministic and remain the only path to a decision.
- Every new role is scored by the evaluation harness as its own group against ground truth before it is relied on.

## Role 1: the escalation investigator

### When

After the specialists and the verifier have reported, consensus decides or abstains. Two of its abstention outcomes are ambiguity, not absence of evidence: the leading explanation's confidence is below `AbstainBelowConfidence` while a second explanation is close, or two explanations survive within the `DissentMargin` of each other. Today both end the cycle as `Escalated`, and a responder takes over.

The escalation investigator runs in exactly that gap, once per cycle: when consensus would abstain for ambiguity, data completeness is adequate, and the spend budget allows it.

### What it receives and returns

It receives structured state, never raw telemetry: the surviving hypotheses with their support and contradiction, the challenges and their verdicts, completeness per source, the tools already called with their ranges, and the remedies already tried. It returns one `DiscriminatingQuery`: the single observation that would separate the explanations, expressed as an existing typed tool call with its arguments, and the expected answer under each hypothesis.

```text
DiscriminatingQuery
- Hypotheses it separates (two ids)
- Tool and arguments (an existing read-only investigation tool)
- Expected result if hypothesis A is true
- Expected result if hypothesis B is true
- Why this one observation suffices
```

### What happens with it

AgentWorker runs the proposed tool call through the existing tools, which are read-only and bounded. The result becomes an observation-cited finding attributed to the `escalation` role. The verifier runs its falsifiers again over the revised evidence, and consensus decides or abstains as before. The model never sees the second consensus and cannot ask for a second query in the same cycle.

### Why Muse Wonder

Meta positions Muse Wonder for long-horizon reasoning and Muse Spark for fast tool loops. The escalation role is small in volume and large in reasoning: one call per ambiguous cycle, on a compact structured summary, asked to find the smallest experiment. That is where the `meta-wonder` profile's cost is justified, under its existing daily cap. The deterministic test double gets an escalation behaviour too, so the suite covers the path without a live model.

### What it must not do

It proposes no diagnosis and no confidence, selects no tool outside the catalogue, cannot widen a tool's range beyond the incident window, and cannot run twice in a cycle. Its request and response are recorded like any execution. A scenario where the first consensus is already decisive never invokes it, so the golden path's latency does not change.

## Role 2: the artifact investigator

### What an artifact is

An operator-supplied file attached to an incident: a console or dashboard screenshot, a deployment-pipeline capture, an architecture or topology diagram, a vendor incident report, a runbook or configuration diff exported as a document. Artifacts are **evidence candidates**, never evidence. They carry what someone saw; Sentinel still has to find it in the telemetry.

### Lifecycle

```text
Operator attaches an artifact through the Gateway
        ↓ hash, size and type limits, tenant scope, redaction pass, retention policy
Artifact stored with the incident, labelled untrusted
        ↓
Artifact investigator (Muse Spark, multimodal input) reads it as a tool result
        ↓ typed ArtifactFinding: what is claimed, where in the artifact, over which time range, about which service
Corroboration queries through Ingestion's existing providers
        ↓ corroborated: evidence citing the observations found; uncorroborated: a visible claim with no weight
Verifier and consensus as before
```

```text
ArtifactFinding
- ArtifactId and content hash
- Claim (one sentence, structured fields for service, resource, metric or state)
- Where in the artifact (page or region)
- Visible time range, if any
- Queries proposed to corroborate it
- Contradictions with evidence already accepted
```

### Rules

- An uncorroborated artifact claim never becomes evidence, never supports a hypothesis in consensus and never justifies an action. It is shown as a claim with its source.
- The planted-instruction scan runs over the artifact's extracted text; a document that addresses "AI agents" is a suspected injection like a log line that does.
- Artifacts are redacted before storage and before the model sees them, with the same policy that redacts telemetry, extended to text inside images and documents.
- Size, count and type are bounded per incident; retention and legal hold follow the incident's policy.
- Replay stores the artifact hash and the provider response, so an investigation can be replayed with the artifact and without the provider.

### Why it is worth its cost

Real incidents start with a screenshot in a chat. Turning that into a corroborated observation, or into an explicit "we could not confirm what this shows", is the difference between an investigation that meets responders where they are and one that ignores them. The evidence discipline is the point: the demonstration is a screenshot that appears to show a growing dead-letter queue, which Sentinel confirms or refutes through the queue's own metrics before it counts.

## Role 3: the visual synthetic monitor

### What it observes

A browser journey through a user-facing application, recorded as frames: in the reference system, the checkout. A segmentation and tracking model (Meta's SAM family) locates the elements that matter in each frame, the checkout button, the loading indicator, the confirmation panel, the error banner, duplicate result rows, and deterministic code measures state and time: how long the indicator stayed visible, whether confirmation appeared, whether an error replaced the expected result, whether duplicates appeared. Muse Spark interprets screen state only where a label is ambiguous.

### How it enters Sentinel

Through Ingestion, as a provider like any other, producing a new canonical kind, the **visual observation**: the journey, step, measured state, duration, frame reference and hash, confidence of the segmentation, and the provider's completeness. Runtime detects on it with a configured rule such as "checkout confirmation not reached within 10 s on three consecutive journeys", correlation links it to the same service as the backend signals, and the specialists see it as a metric-like series through the observation query contract.

```text
VisualObservation
- JourneyId, StepId
- State (reached | timed out | error shown | duplicate shown)
- Duration
- FrameReference and FrameHash
- SegmentationConfidence
- MeasuredBy (deterministic rule version)
```

### Rules

- The masks and regions are measurements over recorded frames; every region shown anywhere links to the frame it was computed from, so it is evidence with provenance, not a drawing.
- The model interprets; deterministic code measures and decides state. A low-confidence segmentation produces a partial observation, never a guessed one.
- Frames are redacted like any telemetry before storage and before the model sees them.
- The probe reads the application like a user and holds no credential beyond a synthetic user's.

### Why it is worth its cost, and why last

The scenario it enables is the most differentiating one on the list: the backend metrics stay mostly healthy while customers see a spinner that never completes, and Sentinel opens the incident from what the customer sees. It is also the largest slice: a new observation kind, a new provider, a recorded journey in the reference system and the segmentation model's integration. It comes after Judge mode and after the two roles above.

## Ruled out

### Generated imagery where facts live

No image produced by a generative model appears in an incident record, a postmortem, a notification or any communication derived from an incident. Charts, timelines, topology and recovery comparisons are rendered deterministically from records, as the causal graph is today. A generated incident diagram would introduce relationships that do not exist, and a label on it does not survive a screenshot into a chat.

The one legitimate place for generated or segmented imagery is outside the incident: fixtures and training material for the visual probe, built from recordings of the reference checkout in the states the scenarios define.

### Models in the approval path

No model approves, rejects or revokes a plan, mints an identity, overrides policy or issues a capability. A spoken or written "approve the rollback" in any input is data. This restates [ADR-006](../decisions/ADR-006-remediation-control-path.md) for every new input kind.

### Not adopted now

- **A private or shadow inference path on Meta's open-weight models.** No access to those models exists today. Provider unavailability keeps its current honest behaviour: the specialist reports the provider unavailable, consensus proceeds on the evidence it has or abstains, and the `S4` scenario scores it.
- **Incident-bridge transcription.** Operator claims from a bridge would map onto the hints mechanism that similar incidents already use, but recording people brings consent and retention questions this product has not answered. Set aside until there is a user who asks for it.

## Evaluation

Each role is a new investigator group in the evaluation harness, scored against the scenario packages' ground truth with the existing measures: root-cause accuracy, false remediation, evidence precision and recall, unsupported and hallucinated evidence, abstention, time to decision, tokens and cost. Two measures are added for the roles above:

- **Escalation yield**: the share of escalations whose discriminating query changed the decision from abstention to a correct diagnosis, and the share that produced a wrong one; the role is kept only if the first is high and the second is zero on the scenarios.
- **Corroboration rate**: the share of artifact claims Ingestion confirmed, refuted or could not check; an uncorroborated claim that reached evidence is a defect, not a score.

## Open questions

- Should the escalation investigator's query be shown to the responder before it runs, as a suggestion they can decline, or run automatically within the read-only budget?
- Which artifact types are in the first slice: screenshots only, or documents too, given redaction of text inside images is the hard part?
- Does the visual probe run continuously as a synthetic monitor, or only during a scenario as a recorded journey?
