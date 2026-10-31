# Phase E03 – Demonstration depth

Enhancement phase. Design source: [Demonstration depth](../architecture/demonstration-depth.md) (Proposed, 2026-10-31). This document is the delivery view; the proposal keeps the capabilities and their boundaries.

## Outcome

Sentinel's judgement can be shown live: it resists an instruction planted in its data, corrects an incomplete fix, recognises a repeat, shows the damage it avoided and lets people watch an investigation as it happens.

## Primary service ownership

- `Sentinel.AgentWorker` scans its own untrusted tool results for instructions to AI agents.
- `Sentinel.Runtime` records them, opens a bounded follow-up cycle after an unrecovered remediation and keeps the remedies an incident tried.
- `Sentinel.Web` shows each of them, and the baseline's counterfactual from the shadow findings AgentWorker already records.
- `Sentinel.Contracts` gains the additive fields they need, versioned under [ADR-012](../decisions/ADR-012-event-semantics.md).
- The reference system gains the faults the new scenarios inject. `Sentinel.Gateway` and `Sentinel.Executor` do not change.

## Entry evidence required

- Phases 05, 06 and 07 are complete: the verifier, policy and approval, and memory and evaluation exist for the scenarios to exercise ([phase status ledger](phase-status.md)).
- The end-to-end suites drive Sentinel.Web in a browser, so a visible capability can be tested where it is shown.

## Workstreams

| Workstream | Work | Evidence when done |
| --- | --- | --- |
| Manipulation resistance | `S9`: an instruction to AI agents planted in order-service's logs during the S1 leak; detection in AgentWorker, the incident's record of it and its card on the incident page | `S9` end to end, web journeys, the scanner's tests |
| Self-correction | `S10`: a compound incident whose first remedy is verified but does not recover it; one follow-up cycle with what was tried, then the remaining cause remedied | `S10` end to end through both approvals and recovery |
| Recurrence | A repeated scenario recognised as seen before, with the earlier incident cited on the page | An end-to-end run of the repeat |
| Damage avoided | The baseline's proposed action and Sentinel's verdict on it, on the incident page | Web journeys; the evaluation report agrees |
| Live investigation | The investigation view updating from the live stream as reports, challenges and the decision arrive | Web journeys with streamed events |

## Exit criteria

The proposal's success criteria, with evidence in the ledger.

## Deferred deliberately

- The audience fault page and guarded autonomy, which the owner set aside on 2026-10-31 ([not pursued](../architecture/demonstration-depth.md#not-pursued)).
- Approvals from chat tools and runs against a real cloud account, which need the owner's external accounts.
- Any capability that would need a live model to pass its tests.
