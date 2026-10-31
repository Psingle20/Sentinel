# ADR-021: AWS emulator for the reference environment

- Status: Accepted
- Date: 2026-10-30
- Decided by: the project owner, with the compatibility gate run against the chosen emulator the same day

## Context

[Phase E01](../implementation/phase-e01-aws-emulated-reference-environment.md) adds a second reference environment in which causes are not deployments and remedies are not rollbacks: a poison message, retry amplification, an ECS service rolled out half-way, a role that lost a permission. It needs SQS, ECS, IAM, STS and, later, CloudWatch behaviour on a developer machine, without an AWS account or paid services. The [proposal](../architecture/aws-emulated-reference-environment.md#floci-and-ministack-evaluation) compared Floci and MiniStack, with LocalStack Community as a known baseline, and set a [compatibility gate](../architecture/aws-emulated-reference-environment.md#compatibility-gate) the emulator must pass before it becomes a dependency.

## Constraints

- No AWS type crosses into Sentinel, and replacing the emulator changes only `reference-system/` and the AppHost ([ADR-011 amendment](ADR-011-reference-system-location.md#aws-emulated-environment-amendment-2026-10-30)).
- The existing reference system stays the default and authoritative environment.
- Credentials are synthetic, and nothing reads a developer's AWS credential chain.
- The image is pinned by digest, and only images the owner approved are pulled.

## Considered options

1. Floci.
2. LocalStack Community.
3. MiniStack.

## Decision

Floci, version 2.3.0 for linux/amd64, digest `sha256:09b72c45f333019adecb302e79c6b746e290135f528016c8514618bc7ab02344` (86.8 MB compressed), pinned in [`reference-system/deploy/aws/emulator.json`](../../reference-system/deploy/aws/emulator.json), which is the only place the version is written. The owner chose Floci over LocalStack and MiniStack, so neither was evaluated; the gate was run against Floci alone, as evidence of what it supports rather than as a comparison.

Floci runs with the Docker socket mounted and a user-defined Docker network, so that ECS tasks start as real containers on that network, with `FLOCI_SERVICES_ECS_IMAGE_PULL_BEHAVIOR=prefer-cached` so that a task never pulls an image that is already local. Every container it starts carries the environment's label, and the environment removes them when it stops, because Floci does not.

### Operation matrix

Verified on 2026-10-30 on Windows 11 with Docker Desktop by the environment conformance tests in [`reference-system/tests/Reference.Tests/Aws/`](../../reference-system/tests/Reference.Tests/Aws/), through the AWS SDK for .NET 4 (`AWSSDK.Core` 4.0.102.15): seven tests, all passing, in 42 seconds.

| Gate check | Result | Evidence |
| --- | --- | --- |
| 1. Pin and start | Pass, outside Aspire for now | Started from the digest in 5.0 s; Aspire integration is Stage 1 |
| 2. Readiness and clean shutdown | Pass, with one gap | `/_localstack/health` answers with every service `running` and version `2.3.0`; the emulator stops cleanly, but an ECS task container it started keeps running |
| 3. SQS, DLQ, ECS metadata, IAM and STS | Pass | Queue with a redrive policy, cluster, task definitions, service, role and assumed-role session |
| 3. CloudWatch Logs and Metrics (gates Stage 3 only) | Pass | A log event and a metric datapoint written and read back |
| 4. A real containerized workload | Pass | An ECS service ran its task as a real container and rolled out a new task definition revision, with every task on the new revision |
| 5. The SDK clients Sentinel's adapters will use | Pass | SQS, ECS, IAM, STS, CloudWatch and CloudWatch Logs clients from the AWS SDK for .NET 4 |
| 6. Reset without stale resources | Pass, with the label cleanup | A restarted emulator in memory mode holds no queue or cluster; the task container that outlived the first emulator was removed by its label, and none remained |
| 7. Account and region isolation | Pass, as namespacing | Queues of accounts 111111111111 and 222222222222, and of `us-east-1` and `eu-west-1`, are invisible to each other; STS resolves the account from a 12-digit access key and an assumed role acts as that role |
| 8. First scenario within budget | Pass, Stage 2 | `AWS-S1` end to end in 7 min 15 s, 10.9 min with the system's start; the emulator at most 78 MiB of its 768 MiB cap and the consumer's task 63 MiB of 256 MiB, sampled during the run (below) |
| 9. No external credentials, telemetry upload or paid services | Pass for credentials and services; upload not verified | Synthetic credentials only; outbound traffic was not blocked, so whether the emulator sends anything out was not observed |
| 10. Known incompatibilities and tested digest | Recorded | Below, and the digest above |

The poison message moved to the dead-letter queue after exactly two receives, as its redrive policy asked, 6.7 seconds after it was sent.

### Stage 1 results, 2026-10-30

Phase E01's Stage 1 ran the same image through the AppHost and closed two of the open checks. Ten environment conformance tests now pass, one class at a time, and leave no container behind.

| Gate check | Result | Evidence |
| --- | --- | --- |
| 1. Pin and start | Pass, through Aspire | `aws-emulator` starts from `emulator.json` under `Sentinel:ReferenceEnvironment=aws`, capped at 768 MB and 1.5 CPUs, and `AWS-S0` passed end to end through the AppHost in 4 min 42 s |
| 4. A real containerized workload | Pass, with the real consumer | The queue-consumer's own image, 233 MB on the pinned ASP.NET base, ran as an ECS task on the emulator's SQS queue, processed its orders and confirmed them with an order-service on the host |
| 6. Reset without stale resources | Pass, through the environment | The AWS environment's reset scales the consumer to zero, purges both queues, proves them empty and serves the baseline revision again, twice in a row; the AppHost removes the emulator's containers by label when it starts and stops |
| 9. No external credentials, telemetry upload or paid services | Pass | On an internal Docker network with no default route, the emulator became healthy and created a queue, sent and received a message over the AWS JSON protocol |

Two more facts the AWS environment depends on:

- Under load, ECS reports a rollout `COMPLETED` before `ListTasks` shows the new task; a check of the replacement waits on the tasks themselves.
- ECS tasks reach the host's endpoints, the OTLP receivers and order-service, through `host.docker.internal`, which Docker Desktop provides. A Linux Docker engine needs a host-gateway entry the task definitions do not yet carry.

Stage 2 added what Sentinel's own AWS adapters rely on, each checked by their emulator tests in Ingestion and Executor, and one incompatibility it found on the way:

- ECS keeps one `PRIMARY` deployment per service and drops the previous one as soon as a rollout starts; its `updatedAt` never moves, and only `rolloutState` turns from `IN_PROGRESS` to `COMPLETED`, about six seconds later. Ingestion's ECS provider therefore remembers what each service served, and times a completion when it first sees it.
- A service's first deployment carries the service's own `createdAt`, so a service deleted and created again by a reset is a creation, not a deployment. A deleted service lingers as `INACTIVE` beside the one that replaces it.
- A redrive policy set on a queue that was created without one applies at the next receive, so a message already past its limit is dead-lettered then; clearing the policy removes it, and setting it again applies again. Executor's SQS quarantine relies on this, so the AWS environment's queue has no redrive policy of its own.
- `ListQueues` lists every queue of the account, and a task definition's environment comes back as registered, so Ingestion reads the topology a consumer's task declares: the services it calls through service discovery and the queues it names.
- The SDK reports a client timeout as a plain `TimeoutException`, not an `AmazonClientException`.
- A rollout replaces the consumer's task, and the replaced task's last pushed gauge stays in Prometheus for its five-minute lookback, since pushed OTLP series get no staleness markers. Read through `max(...)`, the old task's queue age outlived the poison message: `AWS-S1`'s first verified quarantine was judged unrecovered at a frozen 37.66 s. Queue gauges are therefore read over their last 30 seconds, `last_over_time(...[30s])`, which a live task refreshes every five. This is not the emulator's doing; any replaced task that pushes its metrics behaves so.

### Stage 2 budget, 2026-10-30

Gate check 8 ran with `AWS-S1` through the AppHost, on the developer machine that ran every earlier check, with Docker Desktop's 8 GB.

| Measure | Budget, set with this check | Measured |
| --- | --- | --- |
| `AWS-S1`, from its quiet check to verified recovery | 14 min, the test's detection and recovery timeouts | 7 min 15 s |
| The AWS suite, `AWS-S0` and `AWS-S1`, with the system's start | 20 min | 15.1 min for both; 10.9 min for `AWS-S1` alone |
| Emulator memory | its 768 MiB cap | 29 MiB at the start of a run, 78 MiB at its peak |
| Emulator CPU | its 1.5-CPU cap | under 12% while sampled |
| The consumer's ECS task | its 256 MiB task memory | 56 to 63 MiB |
| Containers left after the AppHost stops | none | none |

The figures are samples of `docker stats` during the runs, not a profile, and a slower machine needs more time.

### Stage 4 admission checks, 2026-10-31

Two checks decide which of Stage 4's scenarios the pinned image can carry, kept as [admission tests](../../reference-system/tests/Reference.Tests/Aws/FlociStage4AdmissionTests.cs):

- EventBridge, for `AWS-S7`: a rule delivers a matching event to its SQS target, a changed pattern stops the delivery, and `DescribeRule` and `ListTargetsByRule` report the pattern and target, so route drift is emulator-modelled and readable as a change. `AWS-S7` can be admitted.
- IAM, for `AWS-S4`: policies are stored and versioned, and an approved default version can be restored, but no policy is enforced, so a permission removed from a role changes nothing a workload does. A permission-drift failure could only be faked by the workload itself, so `AWS-S4` is not admitted on this image; it waits for an emulator that enforces IAM or for the real-cloud validation of Stage 5.

`AWS-S7` was built on the EventBridge result. It also relies on these, each checked by the emulator tests of the reference system, Ingestion and Executor:

- `PutEvents` accepts an event whether or not a rule matches it, and a rule delivers the whole event envelope to its SQS target, the order inside it as `detail`; the consumer unwraps it.
- `ListRules` reports each rule's pattern as it was put, so Ingestion's route provider compares the patterns it sees between pulls; EventBridge keeps no history of a rule, so a rule's first sight is its baseline.
- `PutRule` on an existing rule replaces its pattern and keeps its targets, so restoring the approved pattern is one idempotent call.

`AWS-S12`, colliding resources in two accounts, passed gate check 7's account isolation, but Sentinel itself reads one account per Ingestion and routes every investigation and recovery query to one Ingestion, so it waits for Phase 10's multi-tenant operation rather than for the emulator.

### Known incompatibilities

- An ECS task container outlives the emulator that started it; the environment must remove containers by label when it stops, and the conformance fixture does.
- Account isolation is namespacing, not a security boundary: the access key selects the account and signatures are not verified unless `FLOCI_AUTH_VALIDATE_SIGNATURES=true`. Tenant-isolation scenarios such as `AWS-S12` test Sentinel's scoping, not the emulator's.
- The health endpoint does not list STS, which is served, and names CloudWatch metrics `monitoring`.
- There is no reset endpoint. A scenario's reset is a restart in memory mode; snapshots exist only through Floci's separate CLI and are not used.
- ECS reports one `PRIMARY` deployment at a time and `pendingCount` is always 0, so a partial rollout (`AWS-S9`) needs its own check before it is admitted.
- `QueueUrl` values carry the emulator's network hostname, `aws-emulator`; SDK clients reach it through their configured endpoint instead.
- IAM policies are not enforced: a role denied `sqs:*` still calls SQS. Policies, their versions and their default are stored and reported faithfully.
- A redrive policy a queue was created with never changes. `SetQueueAttributes` stores a new one and `GetQueueAttributes` reports it, but receives keep applying the original, so a quarantine that lowers it moves nothing. On SQS the new policy applies at the next receive. Found in Stage 2, when `AWS-S1`'s quarantine timed out on a message received 86 times under a lowered limit of 3, and pinned by a conformance test that fails once a newer image fixes it.

## Consequences

### Positive

- The first scenarios' operations, a poison message on SQS with a dead-letter queue and an ECS service rollout, behave as `AWS-S1` and `AWS-S2` need, so Stage 0b and Stage 1 can start.
- The emulator is small and quick: 87 MB to pull, five seconds to start, 33 MiB at rest.
- The conformance tests stay as the version-bump check: a new digest in `emulator.json` is accepted only when they pass.

### Negative

- Mounting the Docker socket gives the emulator control of the host's containers. It is allowed only on a developer machine or an isolated CI runner, never on a shared privileged host, and never in a production definition.
- The environment owns cleanup of the containers the emulator starts.
- The emulator tests need Docker and the pinned image, so they carry the `Emulator` trait and the default `test` task excludes them; they run with `dotnet test reference-system/tests/Reference.Tests --filter Category=Emulator`.

## Follow-up work

- A host-gateway entry in the consumer's task definition for Linux Docker engines, before CI runs the AWS environment.
- A check of partial ECS rollouts before `AWS-S9` is admitted (Phase E01, Stage 4); EventBridge passed its check for `AWS-S7` on 2026-10-31.

Stage 1's `aws-emulator` resource and gate check 9, listed here when the decision was accepted, were done on 2026-10-30; see the Stage 1 results above.

## Revisit criteria

- A conformance test fails on a version the scenarios need, and the fault is the emulator's.
- Floci's licence or distribution changes so that it can no longer run locally without an account.
- A scenario the catalogue admits needs an operation Floci does not implement. Choosing another emulator then is the owner's decision.
