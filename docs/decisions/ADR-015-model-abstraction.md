# ADR-015: Model abstraction

- Status: Accepted
- Date: 2026-10-28

## Context

`Sentinel.AgentWorker` investigates incidents with Meta models, with Muse Spark as the default. Connection details for the Meta Model API are not yet available, and CI must be deterministic. Model output is an untrusted proposal (Phase 04), and every material claim must cite observations returned by tool calls in the same execution.

## Constraints

- No model SDK type crosses into AgentWorker's domain or into any contract.
- Models request typed tools; they cannot name vendor queries or executable capabilities.
- Invalid or incomplete output leads to repair, retry, fallback or abstention, never silent coercion.
- Every execution records model, prompt version, tool calls and their canonical results for replay (ADR-007, Phase 07).

## Considered options

1. Call the model API directly from investigators.
2. A Sentinel-owned `IModelProvider` with a provider-neutral conversation and tool-call protocol, structured-output validation outside the provider, and a deterministic test double.
3. Adopt a general agent framework as the abstraction.

## Decision

Use option 2.

- `IModelProvider` lives in `Sentinel.AgentWorker.Application`. A request carries the model, prompt version, messages, the tool catalogue as typed definitions and the required output schema name. A response is either tool calls with JSON arguments or a final JSON document, plus token usage and a finish reason. Refusals, timeouts, rate limits and malformed responses are typed outcomes.
- The investigation harness, not the provider, runs the tool loop: it authorizes and bounds each tool call, executes it through the Ingestion query contract, keeps tool results in the data channel and enforces the investigation budget.
- Structured output is parsed and validated by AgentWorker: required fields, ranges, at least two hypotheses for an investigation, and every cited observation ID returned by a tool call in this execution. Validation failure allows one repair attempt and otherwise produces an abstention.
- Implementations:
  - **Deterministic test double**, the default locally and in CI. It drives the same tool protocol with a fixed, rule-based analysis of the tool results and never reads scenario ground truth. Fault modes (malformed, incomplete, refusal, timeout) are selectable for tests.
  - **Meta Model API adapter**, used when the API endpoint, model name and key reference are configured through local secret configuration. Its wire details are confirmed when the Muse Spark details are provided.
- Model selection is configuration (`Model:Provider`, `Model:Name`), so routing to another Meta model changes no domain code.

Option 1 makes CI depend on a live model. Option 3 adds a dependency whose abstractions would leak into the evidence contracts.

## Consequences

### Positive

- CI runs the whole investigation path deterministically.
- Provenance checks are independent of the model, so a fabricated observation ID is rejected for every provider.
- Switching models is a configuration change, recorded in each execution's model metadata.

### Negative

- The test double's analysis is rule-based. It proves the pipeline, not reasoning quality; reasoning quality is measured only with a live model (Phase 07).
- The Meta adapter is unverified against the live API until the Muse Spark details arrive.

## Revisit criteria

Revisit when the Muse Spark API details arrive, if a second service needs model access (ADR-007), or if native structured-output features make AgentWorker-side validation redundant for a provider.
