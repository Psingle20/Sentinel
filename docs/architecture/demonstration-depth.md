# Demonstration depth

Status: Proposed, 2026-10-31. Delivered by [Phase E03](../implementation/phase-e03-demonstration-depth.md).

## Purpose

Sentinel's claims are about judgement: it blames the right cause, refuses the easy wrong answer and does nothing when the evidence is thin. The reference scenarios prove those claims, but most of the proof is in recorded runs and the evaluation report, not in front of an audience. This proposal adds what makes the judgement visible and testable live, without new deployables, new shared packages or any change to the trust boundaries in the [architecture](architecture.md).

## Capabilities

| Capability | What an audience sees | Where it lives |
| --- | --- | --- |
| Manipulation resistance | A service's logs tell AI agents to roll back the wrong service and bypass approvals; Sentinel diagnoses the real cause, proposes the right rollback for approval and shows the attempt on the incident, quoted and ignored | AgentWorker scans its untrusted tool results; Runtime records what it found; Sentinel.Web shows it. Scenario `S9` |
| Self-correction | A first remedy works at its target but the incident is not over; Sentinel sees from telemetry that the signals did not recover, investigates again with that knowledge and remedies the remaining cause | Runtime opens one follow-up cycle after an unrecovered remediation. Scenario `S10` |
| Recurrence | The same failure a second time is recognised as one seen before, and the earlier incident is cited | Incident memory and similar-incident retrieval, which exist; a scripted repeat and its display |
| Damage avoided | Beside every incident, what the deploy-blame baseline would have done and why it would have been wrong | The baseline already runs in shadow and AgentWorker records its findings; the web app shows them beside Sentinel's verdict |
| Live investigation | Specialists' evidence arriving, hypotheses challenged and the decision settling, as it happens | The live event stream and the existing causal graph, rendered as it grows |

## Design

### Self-correction

- When recovery is judged unchanged or degraded after Executor verified the action at its target, Runtime does not escalate. In the same command it records the verdict and reopens the incident for a follow-up cycle, once per incident. The cycle's onset is the end of the observed recovery window, the moment the remaining symptom was known to outlast the remedy.
- `IncidentReopened` names the plan whose remedy did not recover the incident. The incident keeps every remedy it tried, with its verdict, across cycles.
- The follow-up cycle's investigations are told what was tried. Runtime never proposes a remedy it already applied in the incident; a cycle that leads to one escalates with that reason.
- A deployment that a later change to the same service replaced before onset no longer serves, so it cannot explain the symptoms. The test double contradicts it and attributes nothing to it, which is how a follow-up cycle looks past a cause already rolled back.
- The second cycle's workflow is started by the step that reopens it, with the workflow reconciler and detection as the existing safety nets. A second unrecovered remedy escalates, as before.
- `S10` is the compound scenario: the `S1` leak, then the `S2` batch starting about 150 s later, after the first cycle's investigations have read their window. Rolling back checkout works at its target, but Redis stays saturated, so the follow-up cycle pauses the batch.

### Recurrence

Incident memory and similar-incident retrieval already exist. A remembered incident of the same tenant that resolved, matches strongly and shares the cause is shown first on the page as seen before, with a link to it, its cause and what resolved it. It stays a hint, never evidence.

### Damage avoided

AgentWorker already records the deploy-blame baseline's shadow findings with every incident's agent executions. The incident page reads the latest shadow run, names the deployment the baseline would have rolled back and sets Sentinel's own verdict on that change beside it: rejected, challenged or agreed. When the baseline blamed the same change Sentinel diagnosed, the page says they agree. No service stores anything new.

### Live investigation

The incident page already refreshes from the live stream whenever the incident changes. While the incident investigates, a progress panel shows each specialist as requested, reported or timed out, then the verifier's challenge and the decision, each with its time, so the investigation can be watched as it happens.

## Not pursued

The owner narrowed the proposal on 2026-10-31 to the capabilities above. Two were considered and set aside:

- Audience-driven faults: a development-only page in Sentinel.Web that would start a scenario chosen by someone in the room. The scenario runner's commands in the Aspire dashboard already start any scenario.
- Guarded autonomy: low-risk actions run under policy without approval, behind a kill switch. It would change who may act without a person, so it needs its own decision record, and nothing here assumes it.

## Boundaries

- Nothing found in untrusted data becomes evidence, a hypothesis or an action. Manipulation resistance only makes visible what the existing rules already do: tool results are data, findings validation refuses actions in model output, and every action needs policy, a capability and approval.
- A follow-up cycle is bounded: one per incident, only after an action Executor verified at its target whose recovery was judged unchanged or degraded, never after an inconclusive judgement or one that requires compensation.
- The live investigation view only shows what the incident's events already record; it adds no state and no decision of its own.

## Success criteria

1. `S9` and `S10` pass end to end with the deterministic test double and appear in the evaluation report.
2. The incident page shows a manipulation attempt, a follow-up cycle, an earlier similar incident and the baseline's counterfactual without any extra navigation.
3. Every capability above has an end-to-end or web journey test; none relies on a live model to pass.
