# Phase 07 – Incident memory, replay and evaluation

## Outcome

Make every incident reproducible, comparable and useful for future investigations while establishing objective quality gates for AI reasoning and remediation safety.

## Primary service ownership

`Sentinel.Runtime` owns incident history and deterministic domain replay. `Sentinel.AgentWorker` supports captured-tool and model reevaluation; `Sentinel.Api` exposes authorized replay operations. A separate `Sentinel.ReplayWorker` remains optional until measured isolation or load justifies it.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) exported run record with incident memory, replay and the evaluation harness. Skeleton run records remain readable as the first baseline data. The end-to-end suite must pass with memory and replay on the path.

## In scope

- Incident-memory record linking symptoms, topology, changes, evidence, hypotheses, decisions, actions and outcomes.
- Deterministic replay of versioned event streams and captured provider responses.
- Scenario package format for golden, regression and failure cases.
- Postmortem generation from authoritative structured history with human-editable narrative.
- Similar-incident retrieval with tenant and privacy controls.
- AI benchmark suite measuring root-cause accuracy, evidence precision, hallucination, consensus accuracy, time, tokens, cost and false-remediation rate.
- Model, prompt, tool, policy, schema and dataset version capture.
- Simulation/digital-twin interface with the golden reference system as the first concrete implementation.
- Counterfactual and regression scenarios for misleading deployments, provider failure and failed remediation, starting from the Phase 01 suite `S0`–`S3`.
- The deploy-blame baseline evaluated on every scenario alongside Sentinel.
- Retention, deletion, redaction and legal/privacy metadata for stored incident data.

## Replay levels

1. **Domain replay:** rebuild incident state and projections from events.
2. **Investigation replay:** return captured tool results to reproduce reasoning inputs.
3. **Model reevaluation:** rerun a selected model/prompt version against frozen inputs.
4. **System simulation:** rerun the failure against the reference environment.

Results must clearly identify which level was used; deterministic replay must not be claimed for a live nondeterministic model call.

## Primary deliverables

1. Versioned scenario and incident-memory schemas.
2. Replay runner with isolation and side effects disabled by default.
3. Golden and counterfactual scenario suite.
4. AI evaluation harness and baseline report.
5. Postmortem artifact containing timeline, evidence, decision, action and recovery.
6. Similar-incident query path with provenance.

## Evaluation gates

- Evidence claims are scored only when linked to ground-truth observations.
- A correct root cause with fabricated evidence fails.
- Unsafe or unsupported remediation is a critical failure regardless of diagnosis quality.
- Cost and latency are measured alongside accuracy.
- Every benchmark report includes the [deploy-blame baseline](phase-01-reference-system.md#non-ai-baseline) on the same scenarios. A model, prompt or consensus change that is worse than the baseline on false-remediation rate cannot be promoted.
- Dataset changes and benchmark exclusions are versioned and reviewed.

## Test plan

- Byte/semantic stability of captured inputs and version manifests.
- Side-effect isolation during replay.
- Projection rebuild and historical-schema upcasting.
- Postmortem completeness and source-link tests.
- Cross-model/prompt comparison without contaminating the baseline.
- Privacy deletion propagation through derived indexes.

## Exit criteria

- The golden incident can be replayed without calling live providers or executing actions.
- The event stream rebuilds the same authoritative incident state.
- A baseline evaluation report covers accuracy, evidence, safety, latency and cost, compared with the deploy-blame baseline.
- Postmortem facts trace back to the event/evidence history.
- Similar-incident retrieval respects tenant and retention boundaries.

## Deferred deliberately

A complete production digital twin is not required. The interfaces and scenario discipline are established so simulation depth can grow incrementally.

