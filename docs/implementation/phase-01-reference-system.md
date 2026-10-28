# Phase 01 – Reference distributed system

## Outcome

Provide a realistic, deterministic system that can be healthy, broken, observed, remediated and reset. It is both the first product scenario and the permanent integration-test fixture.

The system ships with confounder scenarios and a non-AI baseline from the start, so Sentinel's reasoning is measured against cases in which the obvious answer is wrong.

## Primary service ownership

The reference environment is outside Sentinel and does not count among its five backend services. It supplies the controlled system, telemetry and remediation target that every Sentinel service uses in the first vertical slice.

## Scenario suite

### S1 – `redis-connection-leak` (golden)

```text
checkout-service v2.4.1 deployed
        -> Redis connection leak
        -> active connections rise
        -> checkout latency rises
        -> retries amplify
        -> order-service load rises
        -> checkout SLO burns
```

Correct outcome: roll back `checkout-service` to v2.4.0. Discriminating signal: checkout's own Redis client connections grow after the deployment.

### S2 – `innocent-deploy-redis-saturation` (confounder)

```text
reporting-worker batch starts
        -> its Redis connections approach maxclients
checkout-service v2.4.2 (benign) deployed about 60 s later
        -> checkout Redis connection errors and latency
        -> checkout SLO burns
```

Correct outcome: identify the `reporting-worker` connection surge, or abstain. Pausing the worker is suggested but not executed in initial scope. Rolling back `checkout-service` is a false remediation. Discriminating signals: Redis connections grouped by client name, checkout's flat pool size, and errors that begin before the deployment completes.

### S3 – `reversed-dependency-regression` (confounder)

```text
order-service v1.8.0 deployed with a slow query
        -> order-service latency rises
checkout-service v2.4.3 (benign) deployed about 2 min later
        -> checkout waits on order-service, retries amplify
        -> checkout SLO burns
```

Correct outcome: roll back `order-service` to its previous revision. Rolling back `checkout-service` is a false remediation. Discriminating signals: order-service span latency rises before checkout's, checkout time is spent waiting on order-service, and checkout's Redis connections stay flat. The causal direction is the reverse of `S1` with a similar symptom set.

### S0 – `healthy-noise` (control)

Seeded traffic with normal noise, routine benign deployments and no injected fault. Correct outcome: no incident is opened.

## In scope

- Checkout service, order service and a Redis-compatible dependency.
- A `reporting-worker` Redis client for `S2`, and healthy and faulty `order-service` versions for `S3`.
- Redis connection metrics labeled by client name.
- Stable service names, ownership metadata, environment metadata and dependency topology.
- Controllable deployment of healthy and faulty versions.
- Deterministic failure injection for the connection leak and retry amplification.
- Business and technical signals: request rate, latency, errors, Redis connections, retry count and SLO burn.
- Structured logs and distributed traces correlated across services.
- Deployment and configuration-change events.
- Idempotent reset to a known healthy state.
- Seeded traffic generator with reproducible load profiles.
- Containerized local orchestration and documented start, break, recover and reset procedures.

## Primary deliverables

1. Runnable reference application and dependencies.
2. Healthy and faulty deployment artifacts.
3. Failure-control interface restricted to local/test environments.
4. Traffic generator and deterministic scenario script.
5. OpenTelemetry instrumentation and service-resource attributes.
6. Scenario manifests for `S0`–`S3` describing expected symptoms, cause and recovery signals.
7. Deploy-blame baseline specification and expected baseline outcome per scenario.

## Scenario contract

Each manifest must identify:

- scenario ID and version;
- affected and downstream services;
- injected cause and activation time;
- confounding changes and their timing;
- expected observations and time windows;
- the discriminating signals that make the correct answer derivable from telemetry;
- acceptable noise and nondeterminism;
- expected leading and rejected hypotheses;
- permitted remediation and the actions that count as false remediation;
- expected baseline outcome;
- recovery thresholds and evaluation timeout.

## Non-AI baseline

Sentinel's investigation must beat a deterministic **deploy-blame** heuristic:

> When a service's SLO burn crosses its threshold, blame the most recent deployment to that service or any of its dependencies inside the lookback window, and propose rolling it back. If no deployment is in the window, abstain.

| Scenario | Baseline outcome | Correct outcome |
| --- | --- | --- |
| `S0` | No incident, so the baseline does not run | No incident |
| `S1` | Rolls back `checkout-service` v2.4.1 (correct) | Roll back `checkout-service` |
| `S2` | Rolls back `checkout-service` v2.4.2 (false remediation) | Identify `reporting-worker` or abstain |
| `S3` | Rolls back `checkout-service` v2.4.3, the most recent deployment (false remediation) | Roll back `order-service` |

The baseline is implemented behind the investigator contract in [Phase 01A](phase-01a-walking-skeleton.md), required to be beaten in [Phase 05](phase-05-verification-consensus.md), and scored in every [Phase 07](phase-07-memory-replay-evaluation.md) benchmark report.

## Safety and isolation

- Failure endpoints must be disabled outside explicitly marked development/test environments.
- Reference credentials and data must be synthetic.
- Reset must not depend on manual cleanup.
- The scenario must run in an isolated network and namespace.

## Test plan

- Healthy-path smoke and load tests.
- Failure activation and deactivation tests.
- Trace continuity across checkout, order and Redis calls.
- Assertions that the known symptoms appear within bounded time for every scenario.
- Assertions that each confounder's discriminating signals are present in telemetry.
- `S0` runs without crossing any SLO threshold.
- Reset idempotency and repeated-run tests.
- Resource bounds so the scenario cannot exhaust the developer machine.

## Exit criteria

- A documented command starts the system in a healthy state.
- A documented command runs any of `S0`–`S3`.
- Telemetry visibly demonstrates each scenario's causal sequence and discriminating signals.
- The correct remediation restores the defined recovery signals for `S1` and `S3`, and stopping the batch restores them for `S2`.
- Every scenario can run repeatedly with stable identifiers and bounded timing.
- The baseline specification and its expected outcomes are recorded in the manifests.

## Deferred deliberately

Sentinel does not diagnose the incident in this phase. This phase creates trustworthy ground truth for ingestion, investigation, remediation and evaluation.

