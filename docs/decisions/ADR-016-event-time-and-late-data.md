# ADR-016: Event time, watermarks and late data

- Status: Accepted
- Date: 2026-10-28

## Context

Sentinel decides from telemetry: detection compares SLO samples, investigations cite observations by event time, and recovery is verified only when the data covers the whole observation window ([ADR-007](ADR-007-cross-service-code-and-provider-access.md)). Providers deliver data late and out of order: OTLP exporters batch, Prometheus evaluates rules on an interval, Loki and Jaeger index asynchronously, and a provider outage leaves gaps that are filled later. Phase 02 requires a documented event-time policy.

## Constraints

- Every observation keeps its event time from the source and the time Ingestion received it.
- A query's `ObservedThrough` must never claim completeness that the sources have not reached.
- Late data must not create duplicates, and must not silently rewrite a decision already made.

## Considered options

1. Process in arrival order and ignore event time.
2. Per-source watermarks with a fixed allowed lateness, late data stored and published as late.
3. A stream-processing engine with windowed aggregation.

## Decision

Use option 2.

- **Pull ranges end behind real time.** Each source pulls `[checkpoint, now − lag)`. The lag is the source's allowed lateness: 10 s for metrics (OTLP export and rule evaluation at 5 s), 1 s for deployment events (the deploy controller's ledger is synchronous), 30 s for logs and traces.
- **The watermark is the end of the last successful pull.** It never moves backwards, and it does not move while the provider is failing. A query's `ObservedThrough` is the minimum watermark of the sources the query depends on.
- **Late data is kept.** An observation whose event time is before its source's watermark when it arrives is stored and published with the attribute `late=true`. Deterministic identity makes it a duplicate if it was already seen.
- **Consumers judge in event time.** Runtime's detection streak ignores samples at or before the last one it applied. A decision is never rewritten by late data; a later observation may supersede it through a new decision (Phase 05).
- **Gaps stay visible.** After an outage the checkpoint resumes from where it stopped, up to `MaxRangePerPull` per pull, so the gap is back-filled; until then queries report `Partial` completeness and an `ObservedThrough` before the gap.

Option 1 makes recovery verification unsound. Option 3 adds a deployable for volumes Sentinel does not have.

## Consequences

### Positive

- `ObservedThrough` and `Completeness` are derived from the same watermarks for every caller, so recovery verification and investigators read data the same way.
- Re-pulls after a failure or restart are idempotent.

### Negative

- Detection is at least one metric lag behind real time.
- The allowed lateness is fixed per source; a provider that is slower than its lag produces data marked late.

## Revisit criteria

Revisit if a provider's latency distribution makes the fixed lag either drop data or delay detection measurably, or if ingestion volume requires windowed aggregation inside Sentinel.
