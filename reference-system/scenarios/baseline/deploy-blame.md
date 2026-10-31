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
| `AWS-S1` | queue-consumer v1.4.1, an ECS task definition | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the poison message would keep failing on the old revision |
| `S8` | queue-consumer v1.4.1 | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the old revision books duplicates just the same |
| `S9` | checkout-service v2.4.1 | Roll back checkout-service v2.4.1 → v2.4.0 | Yes: the baseline reads no logs, so the instruction planted in them cannot mislead it either |
| `S10`, first cycle | checkout-service v2.4.1 | Roll back checkout-service v2.4.1 → v2.4.0 | Yes, for the first cause; it has no way to see that the incident did not recover |
| `S10`, follow-up cycle | checkout-service v2.4.1, order-service v1.8.1, then Sentinel's rollback of checkout-service to v2.4.0 | Roll back whichever started last: order-service v1.8.1 → v1.7.0, or, when the rollback started after it, checkout-service v2.4.0 → v2.4.1 | Only by timing: undoing the rollback would redeploy the leak |
| `S11` | checkout-service v2.4.1, then order-service v1.7.2 | Roll back order-service v1.7.2 → v1.7.0 | No: false remediation, and the leak goes on |
| `AWS-S7` | queue-consumer v1.4.1, an ECS task definition; the route change is not a deployment | Roll back queue-consumer v1.4.1 → v1.4.0 | No: false remediation, and the old revision receives nothing through the drifted route either |

A reset between runs restores baselines immediately and is recorded as kind `reset`, which the rule ignores.
