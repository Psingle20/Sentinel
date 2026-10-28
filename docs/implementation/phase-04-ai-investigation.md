# Phase 04 – AI investigation and evidence intelligence

## Outcome

Use Meta models through bounded, typed tools to collect attributable evidence, construct competing hypotheses and expose the reasoning state without granting models operational authority.

## Primary service ownership

`Sentinel.AgentWorker` owns Meta calls, typed tool execution and structured investigation results. `Sentinel.Runtime` dispatches bounded tasks and decides which findings become authoritative evidence; `Sentinel.Api` owns model-related control-plane configuration.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) single generalist investigator and two tools with specialists, the evidence blackboard and the hypothesis lifecycle. The deploy-blame baseline stays available behind the same investigator contract, as the evaluation comparison and as a non-model fallback. The end-to-end suite must pass with the specialists on the path.

## In scope

- Meta Model API adapter behind `IModelProvider`, which is owned inside `Sentinel.AgentWorker` (ADR-007); Muse Spark is the default investigation model.
- Optional model routing policy for more capable or self-hosted Meta models without domain changes.
- Typed tools for logs, metrics, traces, deployments, topology, service catalog and knowledge retrieval. External data is read through the `Sentinel.Ingestion` query contract; AgentWorker holds no provider credentials ([ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md)). Evidence derived from incomplete, truncated or stale query results is marked partial.
- Specialist roles for log, metric, trace, deployment, topology and knowledge investigation.
- Agent execution contract covering objective, context budget, permitted tools, output schema, time/token/cost limits and cancellation.
- Schema-validated structured output for evidence, hypotheses, unknowns and requested follow-up work.
- Evidence blackboard with source, time range, tool-call lineage, supporting/contradicting direction, strength and provenance.
- Hypothesis lifecycle and lineage: proposed, investigating, supported, challenged, rejected, confirmed and superseded.
- Initial causal graph linking changes, symptoms, services, observations, evidence and hypotheses.
- Prompt templates, versioning and model metadata capture.
- Guardrails that keep retrieved text in the data channel and prevent it becoming instructions.

## Investigation flow

```text
Workflow assigns question
  -> specialist receives bounded context and tools
  -> model requests typed tool calls
  -> adapters return untrusted observations
  -> specialist emits validated evidence/hypotheses
  -> blackboard records provenance
  -> workflow decides whether more investigation is needed
```

## Primary deliverables

1. Meta provider adapter, test double and model-routing interface.
2. Typed tool catalog with authorization and tenant scoping.
3. Specialist-agent definitions and reusable execution harness.
4. Evidence and hypothesis stores plus query APIs.
5. Blackboard update events and causal-graph representation.
6. Model-call telemetry for latency, tokens, errors, schema failures and cost estimates.

## Model safety rules

- Model output is an untrusted proposal until validation succeeds.
- Agents receive no production credentials and cannot call action adapters.
- Every tool invocation is authorized, tenant-scoped, logged and bounded.
- Free-form text cannot name an executable capability; only validated typed plans can.
- Retrieved logs, documents and runbooks are quoted/segregated as data.
- Invalid or incomplete output causes repair, retry, fallback or abstention—not silent coercion.

## Test and evaluation plan

- Structured-output validation and malformed-response tests.
- Tool authorization, argument validation and tenant-isolation tests.
- Prompt-injection fixtures embedded in logs and runbooks.
- Model timeout, refusal, rate-limit and partial-output tests.
- Evidence provenance and deduplication tests.
- Golden scenario: expected evidence and at least three plausible hypotheses are produced.
- Confounders `S2` and `S3`: the hypothesis set includes the true cause, and the evidence includes each scenario's discriminating signals.
- Test-double runs remain deterministic for CI.

## Exit criteria

- Specialists independently inspect all golden-scenario signal types.
- Every material claim links to observation IDs and tool-call provenance.
- At least one credible competing hypothesis is recorded, not only the known answer.
- Invalid model output cannot mutate authoritative incident state.
- Agents cannot invoke remediation capabilities.
- Model latency, failure, token and structured-output metrics are visible.

## Deferred deliberately

This phase gathers and organizes reasoning. Adversarial falsification, reliability weighting and the final decision belong to Phase 05.

