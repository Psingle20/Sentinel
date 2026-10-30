# Sentinel's service-level objectives

Sentinel watches other systems' SLOs, and it needs its own: a responder who trusts it to open, diagnose and verify incidents needs to know when it is too slow or too stale to trust (Phase 09). This document defines what Sentinel promises, the indicator that measures each promise, and what happens when a promise is broken.

The objectives are targets for the production deployment. Locally the same indicators are emitted and visible in the Aspire dashboard, but nothing enforces them, and the deterministic test double makes local timings faster than a live model's.

## Objectives

Each indicator is an OpenTelemetry metric every service already exports. Windows are rolling 28 days.

| Promise | Indicator | Objective |
| --- | --- | --- |
| Telemetry is fresh | `sentinel.ingestion.watermark.lag`, per source: how far a source's watermark trails the present after each pull | 99% of pulls trail by at most 60 s |
| Symptoms become incidents quickly | `sentinel.runtime.incident.detection_delay`: from a symptom's onset to its incident's detection or reopening | 95% within 120 s |
| Incidents are decided promptly | `sentinel.runtime.incident.time_to_decision`: from the start of a cycle to its decision, diagnosed or abstained | 95% within 15 minutes |
| Events reach their consumers | The `outbox-lag` readiness check: the oldest unpublished message's age against `OutboxOptions.LagThreshold`, 1 minute by default | Degraded less than 0.1% of the time |
| Responders can reach Sentinel | `http.server.request.duration` on the Gateway, for `/api/v1` excluding the live stream: responses that are not 5xx | 99.5% |

An abstention counts as a decision for the timing objective: abstaining quickly with the reason is the right answer when the evidence is thin. How often Sentinel abstains, and whether its diagnoses are right, are measured by the [evaluation harness](../../scripts/evaluation/README.md) against each scenario's ground truth. That is a release gate, not an SLO, because correctness needs ground truth that production incidents do not carry.

## Indicators without objectives

These are measured and worth watching, but a target would mostly measure people or providers rather than Sentinel:

- `sentinel.runtime.incident.time_to_recovery_verdict` includes the time a person takes to approve.
- `sentinel.runtime.decisions`, `sentinel.runtime.executions`, `sentinel.runtime.recoveries` and `sentinel.runtime.escalations`, by result, show the mix of outcomes; a rising share of abstentions or escalations is a prompt to look at evidence quality, not an outage.
- `sentinel.agentworker.model.duration`, `.failures`, `.schema_failures` and `.cost` show the model provider's latency, reliability and spend. Spend has hard caps of its own ([models and spend](../runbooks/models-and-spend.md)).
- `sentinel.ingestion.pull.failures`, `.source.failures` and `.late` explain a freshness breach when one happens.

## Guardrails with no error budget

Some properties are not traded against speed at all, so they have no budget: one violation is an incident in Sentinel itself.

- Nothing executes without a valid, unused approval attestation for the exact plan version, and the emergency stop and manual overrides win over policy ([ADR-006](../decisions/ADR-006-remediation-control-path.md)).
- No incident is resolved by remediation without recovery measured from the expected signals.
- No model output that cites an observation its tools did not return is accepted.

The tests that guard them are listed in the [threat model](threat-model.md) and the [phase status ledger](../implementation/phase-status.md).

## When a budget is spent

- **Freshness or detection.** Investigate the sources first: their pull failures and lag. While freshness is breached, treat Sentinel's diagnoses as advisory and keep autonomy at approval-required for every environment.
- **Decision time.** Check model latency and failures, then investigation timeouts. Route slow roles to a faster profile, or shorten `InvestigationTimeout` so Sentinel abstains sooner rather than late.
- **Event delivery.** Follow the [dead letters](../runbooks/dead-letters.md) runbook; an outbox that cannot publish usually means the messaging Redis is unhealthy.
- **Availability.** The Gateway is the only way in. Restore it before anything else; the emergency stop is also reached through it, and Executor keeps denying actions while an engaged stop cannot be released.

Raising autonomy for an environment, from approval-required towards constrained autonomous execution, requires every objective to have been met for the preceding window.
