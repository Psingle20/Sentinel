# Phase E04 – Judge mode

Enhancement phase. Design source: [Judge mode](../architecture/judge-mode.md) (Proposed, 2026-10-31, revised the same day). This document is the delivery view: what is built, in which order, how it is tested and when it is done. The design holds the stages, the principles, the security rules and the deferred list; nothing here repeats them.

## Outcome

One incident can be shown as a guided, source-linked story from context to proven recovery, in `Captured` mode from a run record and in `Live` mode from an active incident, built entirely on the records the Gateway already serves. The story stays truthful when Sentinel abstains, is denied, fails or cannot prove recovery, and it adds no service, contract, projection, storage, authorization path or decision logic.

## Primary service ownership

- `Sentinel.Web` owns everything in this phase: the route, stage derivation, playback, disclosure, accessibility and the truth bar.
- `Sentinel.Gateway`, `Sentinel.Runtime`, `Sentinel.AgentWorker`, `Sentinel.Executor`, `Sentinel.Ingestion` and `Sentinel.Contracts` change nothing. A need to change one of them is a sign the slice has grown past the design's first slice and goes back to the design before code.

## Entry evidence required

- The incident page renders the real system, and the Gateway serves the incident detail, timeline, evidence, agent executions, run record and live stream ([Phase 08](phase-08-product-experience.md), [synchronous contracts](../business-contracts/synchronous-contracts.md)).
- Phase E03 has delivered `S9` (an instruction planted in logs, shown and ignored) and `S10` (self-correction after an unrecovered remedy), so the two stories that justify the view exist as run records ([Phase E03](phase-e03-demonstration-depth.md)).
- Phase E02's form, feedback and token work is in place, so the view composes `Field`, `ActionResult`, the status vocabulary and the motion tokens rather than inventing presentation ([Phase E02](phase-e02-frontend-design-system-depth.md)).
- The web app's Playwright suite runs against the mock API with run-record fixtures, so journeys can be tested without the whole system.

## Delivery rule

Build the captured `S9` story first, complete and reviewable on its own, before the live mode and before any further scenario. The captured path has no dependency on a model, a scenario runner or a running environment, so it is the dependable primary journey for a demonstration and the fixed point every later slice must keep green.

## Slices

| Slice | Work | Evidence when done |
| --- | --- | --- |
| 1 – Captured story | The route and its five regions; stage derivation from `IncidentDetail`, the agent executions and the run record; deterministic stage summaries; `Captured` truth bar with original timestamps under accelerated playback; the evidence drawer deep-linking into the workspace; `S1`, `S9` and `S10` run records as fixtures, including the baseline counterfactual and the follow-up cycle | Playwright journeys for all three stories against the mock API; stories and screenshots for every stage status and every ending; axe and reduced-motion checks |
| 2 – Live story | The same derivation fed by the incident queries and the server-sent event stream; follow-newest with manual override; stale and reconnect states; the workspace's own approval dialog shown only to an authorized operator | The browser journey in the end-to-end `S9` run opens the live story and reaches recovery; disconnect and duplicate-event tests |
| 3 – Endings and bounds | Every failure ending in the design's table rendered from recorded state; the long-incident paging the incident page uses; the 768 px tablet pass | Journeys for abstention, denial, rejected approval, failed action and inconclusive recovery from fixtures; tablet screenshots |

The deferred capabilities in the design (proof manifest and signing, export, replay comparison, model-drafted explanation, share links, rollout machinery) are not slices of this phase. Each becomes its own enhancement phase when its decision record exists.

## In scope

- Everything the slices list, inside `apps/Sentinel.Web`.
- Run-record fixtures for `S1`, `S9` and `S10` under the web app's test support, taken from recorded runs and redacted like any other fixture.
- A short section in the web app's README on how to open a captured story and where fixtures come from.
- The design document's open questions answered as the slices settle them, in the design document.

## Primary deliverables

1. The Judge-mode route with its five regions and the stage derivation, unit-tested as a pure function.
2. Captured playback over run records with the truth bar and original timestamps.
3. Live follow over the existing stream with stale, reconnect and authorized-operator behaviour.
4. The three stories and every ending as fixtures, journeys, stories and screenshots.

## Test plan

- Unit tests for stage derivation: every stage from a full record, every status from partial records, supersession when a later decision replaces an earlier one, and every ending.
- Unit tests for the deterministic summaries: each template renders only from typed facts that carry a source reference, and fails when a reference is missing.
- Playwright journeys against the mock API: `S1`, `S9` and `S10` captured; abstention, denial, rejection, failed action and inconclusive recovery from fixtures; keyboard-only traversal of the stage rail and drawer.
- One browser journey in the end-to-end `S9` run that opens the live story, watches the investigation stage advance and reaches the recovery verdict, in the same run as the existing journeys.
- Storybook stories for every stage status and ending, with the axe and screenshot checks in both themes and at the tablet width.
- No test calls a live model; a separately recorded live-model run record may serve as a fixture.

## Exit criteria

- Captured `S9` plays from context to verified recovery with the environment, scenario runner and models offline, and every claim opens its source.
- Live `S9` advances only from recorded events and reaches the same ending as the workspace.
- `S10` shows both cycles, the unrecovered verdict and the correction.
- `Live` and `Captured` are distinguishable throughout, and captured mode cannot mutate anything.
- Every ending in the design's failure table renders from fixtures and passes its journey.
- The route passes desktop and tablet journeys, axe and screenshot checks, reduced-motion coverage and the long-incident bounds.
- No service, contract, projection or storage changed; the architecture tests and the end-to-end suite stay green.

## Deferred deliberately

- Everything in the design's deferred table, each pending its own decision and phase.
- Starting a scenario or fault from the view; scenario control stays with the reference system's own commands.
- A mobile layout below the tablet width.
- Renaming the view for customers; the open question is answered in the design document when the product name is chosen.
