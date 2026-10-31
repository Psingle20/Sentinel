# Phase E07 – Visual synthetic monitor

Enhancement phase. Design source: [Model roles beyond the specialists](../architecture/model-roles.md), role 3, under [ADR-024](../decisions/ADR-024-model-roles.md) (Proposed, 2026-10-31). This document is the delivery view; the design holds the `VisualObservation` shape, the measurement rules and the provenance rule for frames.

## Outcome

A recorded browser journey through the reference checkout becomes canonical observations: a segmentation and tracking model locates the elements that matter in each frame, deterministic rules measure state and time, and Ingestion publishes the result like any signal. Runtime opens an incident when the customer-visible journey fails even while backend telemetry looks healthy, and recovery is judged on the journey succeeding again. Every region shown anywhere links to the frame it was measured from.

## Primary service ownership

- `reference-system` owns the recorded journey: a browser probe against the checkout, the frames it stores, and the scenario in which the backend stays mostly healthy while the customer sees a spinner that never completes.
- `Sentinel.Ingestion` owns the visual provider: frame intake, redaction of frames, the segmentation call behind a provider interface, the deterministic state and timing rules, and publication of `VisualObservation`s.
- `Sentinel.Contracts` gains the `Visual` observation kind and the `VisualObservation` shape.
- `Sentinel.Runtime` gains a detection rule on the new kind, as configuration, and recovery criteria on it; correlation links the journey to its service.
- `Sentinel.AgentWorker` gains a journey tool over the observation query contract and prompt guidance for customer-visible failures.
- `Sentinel.Web` shows the frame and its measured regions as evidence, linked to the frame hash.
- `Sentinel.Gateway` and `Sentinel.Executor` change only to carry the new kind through existing contracts.

## Entry evidence required

- Judge mode's first slice and Phases E05 and E06 are delivered, so the roles and the presentation this scenario depends on exist.
- Ingestion's provider conformance suite covers a new provider's identity, bounds, completeness and redaction ([Phase 02](phase-02-telemetry-ingestion.md)).
- Detection and recovery are configuration ([Phase 03](phase-03-incident-runtime.md), [Phase 06](phase-06-safe-remediation.md)).
- The reference checkout has a user-facing page a browser can drive, or one is added to the reference system in this phase's first slice.

## Slices

| Slice | Work | Evidence when done |
| --- | --- | --- |
| 1 – Journey and fixtures | A browser probe that records the checkout journey as frames; recordings of the healthy and the failing states the scenarios define; segmentation masks over those recordings as the probe's test fixtures | Fixtures with masks for every state; a replayable recording per state |
| 2 – Provider and observation | `Visual` observation kind and `VisualObservation`; the Ingestion provider with frame redaction, the segmentation model behind its interface, deterministic state and timing rules, completeness and provenance; conformance tests | Conformance suite passes; the recordings produce the expected observations without a live model (fixture masks) and with one |
| 3 – Scenario | The reference scenario in which the backend stays healthy and the journey fails; the detection rule and recovery criteria; the AgentWorker journey tool; the incident page and Judge mode showing frames with linked regions | The scenario passes end to end with the test double; the incident opens from the visual signal before any backend rule fires; recovery is judged on the journey |

## In scope

- One journey, the checkout, with the states the scenario defines.
- The segmentation and tracking model behind a provider interface, so the fixture masks stand in for it in tests.
- The scenario package, the harness entry and a recorded run.

## Primary deliverables

1. The recorded journey, its fixtures and masks in the reference system.
2. The `Visual` observation kind, the provider and its conformance tests.
3. The scenario, detection rule, recovery criteria and journey tool.
4. Frames and regions shown as evidence with frame provenance, in the incident page and Judge mode.

## Test plan

- Reference system: the probe records deterministic frames for each state; the fault that makes the journey fail while the backend stays healthy is in one named file.
- Ingestion: conformance for the new provider; redaction of frames; state and timing rules against the fixture masks, including a low-confidence segmentation producing a partial observation; publication under ADR-018's scope rules.
- Runtime: the detection rule opens the incident on consecutive failed journeys; correlation links it to the checkout service; recovery criteria use the journey.
- AgentWorker: the journey tool returns observations with frame references; findings cite them.
- End to end: the scenario with the test double; the harness scores it; the evidence drawer opens a frame and its regions.

## Exit criteria

- The scenario opens an incident from the visual signal with backend metrics healthy, diagnoses the cause, and judges recovery on the journey, end to end with the test double.
- Every visual observation and every region shown links to its frame and hash.
- The provider passes the conformance suite and works with fixture masks in tests and with the live model in a recorded run.
- The harness report includes the scenario and its baseline comparison, and the ledger records the runs.

## Deferred deliberately

- Continuous synthetic monitoring in production; this phase delivers the scenario and the provider.
- Journeys beyond the checkout, and applications beyond the reference system.
- Any generated imagery; masks and frames are measurements over recordings.
