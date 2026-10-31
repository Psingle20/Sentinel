# Models, spend and demonstrations

Sentinel investigates with the deterministic test double unless a live model is configured ([ADR-015](../decisions/ADR-015-model-abstraction.md)). Live Meta models cost money, so every call is costed and capped. This runbook covers switching models, preparing a demonstration with the strongest model, checking spend, and what to do when a cap is reached.

## The profiles

AgentWorker's [appsettings.json](../../services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Api/appsettings.json) defines the Meta profiles and their list prices. Local routing lives in AgentWorker's user secrets, beside the API key:

| Setting | Everyday value | Demonstration value |
| --- | --- | --- |
| `Model:Provider` | `meta` | `meta` |
| `Model:Routing:traces`, `Model:Routing:topology` | `meta-deep` | `meta-deep` |
| `Model:Routing:changes` | `meta-deep` | `meta-wonder` |

`meta-wonder` (`muse-wonder-1.0`) costs about three to five times as much as Muse Spark and has its own cap of $5 a day.

## Switch to live models, or back

Set the routing with `dotnet user-secrets` against AgentWorker's project, then restart AgentWorker. To go back to the test double, set `Model:Provider` to `test-double` and remove the routing entries, or leave them and rely on the end-to-end suite's override, which always uses the test double unless `SENTINEL_E2E_MODEL_PROVIDER` says otherwise.

```text
dotnet user-secrets set Model:Routing:changes meta-wonder --project services/Sentinel.AgentWorker/src/Sentinel.AgentWorker.Api
```

## Prepare a demonstration

1. The day before, check spend (below) and confirm the remaining total covers the run, about $0.50 to $1.50 per live incident with Wonder on the changes role.
2. Route `changes` to `meta-wonder` and restart AgentWorker.
3. Run the golden scenario once as a rehearsal. Its executions, each with its estimated cost, are listed by `GET $GATEWAY/api/v1/incidents/<incident id>/agent-executions`.
4. After the demonstration, route `changes` back to `meta-deep`.

## The escalation role

When consensus would abstain because two explanations stand too close to choose between, Runtime asks the `escalation` investigator for the one observation that would separate them ([Phase E05](../implementation/phase-e05-escalation-investigator.md)). It runs at most once per incident cycle, only for that kind of abstention and only on evidence at least 80% complete. It makes one model call, or two when its first proposal is refused, over a short structured prompt with no telemetry in it, and AgentWorker runs the one read-only query it proposes. An incident that never reaches an ambiguous decision costs nothing more.

- It runs on whatever `Model:Provider` names unless routed. For a demonstration, route it to the strongest model: `Model:Routing:escalation` = `meta-wonder`. Its calls count against `meta-wonder`'s own daily cap like any other role's.
- To keep it running without spending, route it to the test double: `Model:Routing:escalation` = `test-double`.
- To switch it off, set `investigation.escalation` to `false` in Runtime's configuration from the control plane, or `Runtime:Investigation:Escalation:Enabled` to `false` in Runtime's configuration. An ambiguous consensus is then recorded as an abstention and escalates to responders at once, as before the role existed.

The evaluation report's Escalation section shows how often an escalation led to a correct diagnosis and whether it ever led to a wrong one. The role stays enabled only while it has led to none.

## Check spend

Every execution records its estimated cost. Sum it from AgentWorker's database:

```sql
select date_trunc('day', started_at) as day, record ->> 'model' as model,
       round(sum((record ->> 'estimatedCost')::numeric), 2) as dollars
from agentworker.executions
group by 1, 2
order by 1 desc, 2;
```

The estimates use list prices and are close to the bill but are not the bill; Meta's console is authoritative.

## A cap was reached

An investigation that hits a cap abstains, and its reason names the cap, for example "The daily model spend cap is reached ($15.00 of $15.00)". Other investigations continue on models that are still within their caps.

- A daily cap resets at midnight UTC.
- To raise a cap, change `Model:Budget` (`TotalLimit`, `DailyLimit`, or `ModelDailyLimits:<model>`) in AgentWorker's configuration and restart it. Raise the total only with the credit to back it.
- The caps count recorded executions in AgentWorker's local database, so recreating that database resets the count. The provider's own spending limit, where the console offers one, is the backstop.
