# Deploy-blame baseline

The deterministic, non-AI heuristic Sentinel's investigation must beat ([Phase 01](../../../docs/implementation/phase-01-reference-system.md#non-ai-baseline)). It is implemented behind the investigator contract in `Sentinel.AgentWorker` (Phase 01A) and scored in every Phase 07 benchmark.

## Rule

When a service's SLO burn crosses its detection threshold:

1. Collect every deployment that **started** inside the lookback window ending at detection time, to the affected service or to any service it depends on. Configuration changes and resets are not deployments. The window is 10 minutes.
2. If the set is empty, **abstain**.
3. Otherwise blame the deployment with the latest start time. Ties are broken by service name, ordinal.
4. Propose rolling the blamed service back from the blamed version to the version it replaced.

The baseline uses only deployment events and the dependency topology. It never looks at metrics other than the detection signal, so it cannot tell a cause from a coincidence.

## Inputs

| Input | Source |
| --- | --- |
| Detection time and affected service | The incident Sentinel opened |
| Deployments | Ingestion's deployment observations, through the observation query contract |
| Dependencies | `checkout-service` depends on `order-service`; both depend on Redis, which is not a deployable; `queue-consumer` depends on nothing deployable |

## Expected outcomes

Each manifest records the baseline's expected outcome in its `baseline` field.

| Scenario | Deployments in the window at detection | Baseline outcome | Correct? |
| --- | --- | --- | --- |
| `S0` | none that matter | No incident, so the baseline does not run | Yes |
| `S1` | checkout-service v2.4.1 | Roll back checkout-service v2.4.1 → v2.4.0 | Yes |
| `S2` | checkout-service v2.4.2 | Roll back checkout-service v2.4.2 → v2.4.0 | No: false remediation |
| `S3` | order-service v1.8.0, then checkout-service v2.4.3 | Roll back checkout-service v2.4.3 → v2.4.0 | No: false remediation |
| `S6` | queue-consumer v1.4.1 | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the poison message would keep failing on the old revision |
| `S7` | queue-consumer v1.4.1 | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the old revision retries order-service just the same |
| `S8` | queue-consumer v1.4.1 | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the old revision books duplicates just the same |

A reset between runs restores baselines immediately and is recorded as kind `reset`, which the rule ignores.
