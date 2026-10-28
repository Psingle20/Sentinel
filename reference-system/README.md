# Reference system

The controlled distributed system Sentinel observes and remediates (Phase 01, [ADR-011](../docs/decisions/ADR-011-reference-system-location.md)). It is not one of Sentinel's services: Sentinel reaches it only through its telemetry backends and the deploy controller's API, and nothing under `services/` references this directory.

```text
traffic (scenario-runner) -> checkout-service -> order-service
                                  |                 |
                                  +---- Redis ------+      reporting-worker -> Redis
deploy-controller: switches service revisions, publishes deployment events
redis-exporter:    Redis connections by client name
Prometheus, Loki, Jaeger: the telemetry Sentinel.Ingestion reads
```

| Path | Content |
| --- | --- |
| `services/Reference.Checkout` | checkout-service. v2.4.1 leaks a Redis connection on every second session (S1). |
| `services/Reference.Orders` | order-service. v1.8.0's lookup slows by 4 ms for every second it serves (S3). |
| `services/Reference.ReportingWorker` | reporting-worker. Its batch fans out Redis connections until Redis nearly reaches `maxclients` (S2). |
| `services/Reference.Common` | Raw RESP client and pool, revision control, failure-control guard, telemetry export. |
| `deploy-controller/Reference.DeployController` | Deploys, rolls back and resets revisions; idempotent on `Idempotency-Key`; the target of Executor's rollback adapter. |
| `deploy/redis-exporter` | Exports `reference_redis_clients{client_name}` from `CLIENT LIST`. |
| `deploy/prometheus`, `deploy/loki` | Backend configuration, including the checkout SLO recording rules. |
| `scenarios/manifests` | Ground truth for `S0`–`S3` (Phase 01 scenario contract). |
| `scenarios/baseline/deploy-blame.md` | The non-AI baseline and its expected outcomes. |
| `scenarios/Reference.Scenarios` | The scenario runner: seeded traffic, reset, timeline execution. |
| `tests/Reference.Tests` | Unit tests and Redis behaviour tests against a real Redis container. |

## Run

`dotnet run --project deploy/local/Sentinel.AppHost` starts the reference system with Sentinel. To start the reference system alone, add `Sentinel:ReferenceOnly=true`:

```powershell
dotnet run --project deploy/local/Sentinel.AppHost -- Sentinel:ReferenceOnly=true
```

In the Aspire dashboard, the `scenario-runner` resource has commands **Run S0** to **Run S3**, **Reset to healthy** and **Stop reporting batch**. Each run resets to the baseline revisions, warms up with healthy traffic for 75 s, then executes the manifest's steps at their offsets.

The same operations are HTTP endpoints on the scenario runner, guarded by the generated control token (`reference-control-token` in the AppHost's user secrets):

| Endpoint | Purpose |
| --- | --- |
| `GET /api/scenarios`, `GET /api/scenarios/{id}` | Manifests |
| `POST /api/runs` `{ "scenarioId": "S1" }` | Start a run (one at a time) |
| `GET /api/runs/{runId}`, `GET /api/runs/current` | Run progress, with each step's execution time |
| `POST /api/runs/{runId}/stop` | Stop a run between steps |
| `POST /api/reset` | Return every service to its baseline revision and stop the batch; idempotent |
| `POST /api/actions/stop-batch` | Stop the reporting batch: S2's recovery action |
| `PUT /api/traffic` `{ "requestsPerSecond": 10, "paused": false }` | Adjust the seeded traffic |

## Safety

- Failure controls (revision switches, batch control, reset) exist only in the Development or Test environment with `Reference:FailureControls:Enabled`, and every call needs the control token.
- Deployments need the deploy token (held by the scenario runner and Sentinel.Executor); reading deployments needs the read token (held by Sentinel.Ingestion). All tokens are generated per machine by the AppHost and never committed.
- Resource bounds: Redis `maxclients` is 200, traffic is capped at 50 requests per second with at most 50 in flight, Prometheus keeps 2 hours and Loki 24 hours.
- All data is synthetic.

## Verify

```powershell
dotnet test reference-system/tests/Reference.Tests
dotnet test deploy/local/Sentinel.EndToEnd.Tests --filter "FullyQualifiedName~ReferenceScenarioTests"
```

The second suite starts the reference system alone and checks, against Prometheus, that each scenario's symptoms and discriminating signals appear in bounded time, that the permitted remediation restores the recovery signals, that `S0` never burns the SLO, and that reset is idempotent.
