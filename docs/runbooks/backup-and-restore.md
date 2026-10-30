# Backup and restore

Locally, every Sentinel database lives on the AppHost's one PostgreSQL server, `postgres`, kept on a Docker data volume unless `Sentinel:EphemeralStorage` is set: one database per service ([ADR-009](../decisions/ADR-009-persistence-baseline.md)) plus Temporal's `temporal` and `temporal_visibility`. The messaging Redis keeps its streams on its own volume with an fsync on every write ([ADR-008](../decisions/ADR-008-event-transport.md)). The reference system keeps nothing there. Production backup, with point-in-time recovery from the managed database, arrives with the deployment definition ([Phase 09](../implementation/phase-09-production-hardening.md)).

Use this runbook to take a backup, or to put Sentinel back to a backup after its databases were lost or damaged.

## What a restore brings back

Each database returns to the moment its part of the backup was taken. The messaging Redis is not restored, so its streams and consumer positions stay current.

| Database | Holds | After restoring it to an earlier point |
| --- | --- | --- |
| `runtime` | Incident event streams, which are the truth, with projections, correlation state, incident memory, outbox and inbox | Incidents return to their state at the backup. Outbox rows not yet published then are published again, and consumers' inboxes and idempotent commands absorb any repeat. An approval made after the backup has to be made again. |
| `temporal`, `temporal_visibility` | Workflow progress only ([ADR-005](../decisions/ADR-005-durable-workflow-engine.md)) | Runs resume from where they were. Runtime starts a workflow for any open incident that has none within a minute ([WorkflowReconciler](../../services/Sentinel.Runtime/src/Sentinel.Runtime.Infrastructure/Workflows/WorkflowReconciler.cs)). |
| `ingestion` | Source checkpoints, watermarks and canonical observations | Each source pulls again from its restored checkpoint. Observation IDs are deterministic, so what is pulled twice is stored once, and what happened since the backup is observed again. |
| `agentworker` | Agent executions with their tool calls, captured inputs, cost and replays | Executions since the backup are gone from replay, and so is their cost: the spend caps count only what the restored database recorded. |
| `executor` | Policy versions, operator controls, execution requests, consumed attestations, fences and capabilities | **Operator controls revert**, so an emergency stop engaged after the backup is released. An execution since the backup is forgotten, but the target is not: a repeated rollback is refused because the target no longer serves the revision it rolls back from. |
| `api` | Approvals, published signing keys, the audit log, idempotency keys, notifications, narratives and analytics | Approvals and audit entries since the backup are gone. The key that was current at the backup can no longer sign, and the Gateway retires it at its first signing, as after any restart ([signing key rotation](signing-key-rotation.md)). |

## Back up

Backups hold incident data, redacted but still sensitive. Write them outside the repository and never commit or share them.

1. Find the PostgreSQL container: `docker ps --filter "name=postgres" --format "{{.Names}}"`.
2. Dump every database, roles included, into one file:

   ```bash
   docker exec <postgres container> pg_dumpall -U postgres > <backup directory>/sentinel-<UTC time>.sql
   ```

   The dump runs online. It copies each database in turn, so the databases are each consistent but a few seconds apart; the restore table above is what absorbs that.

## Restore

Restoring replaces every Sentinel database with the backup's. Take a backup of the current state first, so the restore can itself be undone, and restore all databases from one backup, never one service's alone.

1. In the Aspire dashboard, stop the five services and `temporal`, in any order. Keep `postgres` and `messaging` running.
2. Drop the seven databases the backup replaces. This deletes their current contents, so take the backup of the current state before it. `with (force)` disconnects the AppHost's own health checks, which would otherwise hold the databases open, and each statement needs its own `-c` because a database cannot be dropped inside a transaction:

   ```bash
   docker exec <postgres container> psql -U postgres -c "drop database if exists api with (force)" -c "drop database if exists ingestion with (force)" -c "drop database if exists runtime with (force)" -c "drop database if exists agentworker with (force)" -c "drop database if exists executor with (force)" -c "drop database if exists temporal with (force)" -c "drop database if exists temporal_visibility with (force)"
   ```

3. Load the backup, which creates each database again from its contents:

   ```bash
   docker exec -i <postgres container> psql -U postgres -v ON_ERROR_STOP=0 -f - < <backup directory>/sentinel-<UTC time>.sql
   ```

   An error that the role `postgres` already exists is expected; any other error means the restore is incomplete, so stop and investigate before starting a service.
4. Start `gateway-service` and `executor-service`, then check the operator controls with `GET $GATEWAY/api/v1/admin/controls`. Engage the [emergency stop](emergency-stop.md) now, even if the backup had it released, and keep it until the checks below pass. Executor cannot execute anything yet, because every execution first needs Runtime's start confirmation.
5. Start `temporal`, then `runtime-service`, `ingestion-service` and `agent-worker-service`.
6. If the backup is older than the last live model run, lower `Model:Budget:TotalLimit` by the spend since the backup, or keep the test double until the caps are right ([models and spend](models-and-spend.md)).

## Verify

1. Every service and `temporal` report healthy in the dashboard, and the source health page shows each source pulling.
2. `GET $GATEWAY/api/v1/incidents` lists the incidents of the backup. For each open one, a domain replay ([projection rebuild](projection-rebuild.md#check-first)) reports `projectionMatches: true` and every decision matching; if not, rebuild the projections.
3. The approval key set lists one key without `exp` ([signing key rotation](signing-key-rotation.md#check-the-published-keys)).
4. Release the emergency stop. Approvers approve again any plan whose approval was made after the backup; the plan executes once and its incident reaches a recovery verdict, which shows its workflow resumed.

## Tests and open evidence

[IncidentWorkflowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/IncidentWorkflowTests.cs) restore a `pg_dumpall` of Temporal's databases into a new, empty server and resume an in-flight run through approval, execution and recovery. [RuntimeRestoreTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeRestoreTests.cs) restore a `pg_dump` of Runtime's database into a new, empty server: the in-flight incident's stream and projection are the dump's, nothing written after the dump is there, the incident goes on from the restored copy, and a writer behind its stream is still refused. A projection rebuild reproduces the projections from the streams ([RuntimeFlowTests](../../services/Sentinel.Runtime/tests/Sentinel.Runtime.Tests/RuntimeFlowTests.cs)), the rollback precondition is in [ExecutionService](../../services/Sentinel.Executor/src/Sentinel.Executor.Application/Services/ExecutionService.cs), and a Gateway restart retiring its previous key is in [LocalApprovalSignerTests](../../services/Sentinel.Gateway/tests/Sentinel.Gateway.Tests/LocalApprovalSignerTests.cs).

A whole-system drill, every database restored together with an incident in flight, is not yet recorded. It is a Phase 09 exit criterion, and until it passes treat the steps above as designed rather than proven.
