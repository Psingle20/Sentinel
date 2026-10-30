# Phase E01 – AWS-emulated reference environment

Enhancement phase. Design source: [AWS-emulated reference environment](../architecture/aws-emulated-reference-environment.md) (Proposed, 2026-10-30). This document is the delivery view of that proposal: what is built, in which order, how it is tested and when it is done. The proposal keeps the design, the scenario catalogue, the fidelity model and the emulator comparison; nothing here repeats them.

## Outcome

Sentinel is exercised against causes that are not deployments and remedies that are not rollbacks, first in the existing reference system with a plain queue, then in an AWS-compatible environment whose ECS, SQS, IAM and EventBridge semantics let the same Sentinel contracts drive AWS-shaped adapters. The evaluation report gains scenarios in which the deploy-blame baseline is wrong for a new reason, and the reference system gains a second environment behind one abstraction, without a sixth service, a new shared package or any AWS type in a Sentinel contract.

## Primary service ownership

The reference environment stays outside Sentinel's five services ([ADR-011](../decisions/ADR-011-reference-system-location.md)). Inside Sentinel the work is confined to the places the proposal names:

- `Sentinel.Ingestion` owns the AWS change, topology and queue-attribute adapters, and later any CloudWatch adapters, with their credentials.
- `Sentinel.Executor` owns the typed AWS remediation adapters and their credentials.
- `Sentinel.Runtime` gains detection rules, a plan template and recovery criteria for the queue scenarios, as configuration and plan code, not new detection code.
- `Sentinel.AgentWorker` gains one queue tool and prompt guidance.
- `Sentinel.Contracts` gains the new action kind and hypothesis keys, versioned under [ADR-012](../decisions/ADR-012-event-semantics.md).
- `Sentinel.Gateway` and `Sentinel.Web` change nothing beyond displaying the new action kind.

## Entry evidence required

- Phases 01, 06 and 07 are complete and the evaluation harness passes its gate, so a new scenario has a baseline, a report and a replay to land in ([phase status ledger](phase-status.md)).
- Phase 02's provider conformance tests exist, so an AWS adapter has a suite to pass ([Phase 02](phase-02-telemetry-ingestion.md)).
- The proposal's [compatibility gate](../architecture/aws-emulated-reference-environment.md#compatibility-gate) has been run as a time-boxed spike and its operation matrix is written, before any emulator becomes a dependency.
- The ADR-011 amendment for the layout and ADR-021 for the emulator exist before Stage 1 code is written ([planning rules](README.md#planning-rules)).

## Workstreams

The proposal's [delivery stages](../architecture/aws-emulated-reference-environment.md#delivery-stages) are the workstreams. Stage 0b has no emulator dependency and is the first to deliver evidence.

| Stage | Work | Evidence when done |
| --- | --- | --- |
| 0 – Decision evidence | One-day emulator spike (Floci, LocalStack as baseline, MiniStack if needed); operation matrix; ADR-011 amendment; ADR-021; developer resource budget; CI privilege constraints | Matrix and ADRs under `docs/decisions/` |
| 0b – Scenario diversity without the emulator | A queue in the existing reference system; scenarios for the poison message, retry amplification and duplicate delivery as the next `S` numbers; the product-side work for the poison message: action kind, Executor adapter, Runtime detection rule, plan template and recovery criteria, AgentWorker queue tool | Scenarios in the evaluation report with their baseline comparison; end-to-end tests |
| 1 – Healthy AWS environment | `aws-emulator` through Aspire behind `Sentinel:ReferenceEnvironment=aws`; `IReferenceEnvironment` with Local and Aws implementations; provisioning of SQS, DLQ and the ECS-hosted `queue-consumer`; `AWS-S0`; deterministic reset | Environment conformance tests pass against the pinned image |
| 2 – One vertical scenario | `AWS-S1` end to end with the change-and-topology adapter, queue attributes as metrics and the SQS quarantine adapter; telemetry through the existing OpenTelemetry path | `AWS-S1` passes end to end and appears in the evaluation report |
| 3 – Product integration quality | CloudWatch adapters only if the matrix supports them; provider conformance suites; web journey coverage; fidelity labels in run records; replay of captured runs | Conformance and replay tests; fidelity in the report |
| 4 – Scenario expansion | IAM drift, EventBridge drift and tenant isolation (`AWS-S4`, `AWS-S7`, `AWS-S12`) before breadth | Each admitted by the proposal's admission rules |
| 5 – Real-cloud validation | A small subset against an isolated AWS test account, when production claims require it | Recorded divergences |

## In scope

- The two-track scenario catalogue, the hybrid telemetry path and the identity mapping (account to tenant, region to environment) as the proposal defines them.
- The reference-system layout the proposal gives: `Reference.QueueConsumer`, `Reference.Scenarios.Infrastructure`, `reference-system/deploy/aws/`, manifests and packages beside the existing ones, and `AwsReferenceEnvironmentExtensions.cs` in the AppHost.
- Provider and remediation adapters in their owning services, named and placed like the existing ones.
- Scenario packages, baseline outcomes and evaluation-report rows for every admitted scenario.
- Runbooks for the queue consumer under `reference-system/knowledge/` so the knowledge investigator has something to read.

## Primary deliverables

1. The emulator operation matrix and the two decision records.
2. The queue-track scenarios running in the existing reference system, with manifests, packages and baseline outcomes.
3. The queue-related product changes in Contracts, Executor, Runtime and AgentWorker.
4. The environment abstraction and the AWS environment implementation in the scenario runner.
5. The AWS reference environment in Aspire, selectable by configuration, with resource caps.
6. `AWS-S0` and `AWS-S1` with their adapters, tests and evaluation packages.
7. Run records that carry environment, emulator image digest, fidelity labels and the leaked-resource check.

## Test plan

- Unit and contract tests beside the owning code, as the proposal's [testing strategy](../architecture/aws-emulated-reference-environment.md#testing-strategy) lists them: manifest validation, response translation, typed action validation, environment lifecycle.
- Provider conformance suites for every new Ingestion adapter and `IRemediationAdapter` tests for every new Executor adapter.
- Environment conformance tests in `reference-system/tests/Reference.Tests` behind an emulator-required trait, so the default `test` task does not need the emulator.
- Scenario tests in the end-to-end suites under `deploy/local/Sentinel.EndToEnd.Tests`, with the deterministic test double, for every admitted scenario; live-model runs recorded separately through the evaluation harness.
- The architecture tests keep rejecting any reference from `reference-system/` to a `Sentinel.*` package and from `services/` to the reference system.

## Exit criteria

The proposal's [success criteria](../architecture/aws-emulated-reference-environment.md#success-criteria) are the exit criteria. In ledger terms:

- The queue-track scenarios run in the existing reference system and appear in the evaluation report with their baseline comparison, before the emulator is adopted.
- Aspire starts either environment from one configuration value; the existing environment remains the default.
- `AWS-S0` and `AWS-S1` run repeatedly from clean state within the recorded budget, and reset proves nothing leaked.
- Sentinel diagnoses the poison message, rejects the innocent deployment, executes only the approved typed quarantine and verifies recovery from telemetry; the baseline chooses the wrong action.
- Run records carry fidelity labels and never claim AWS equivalence.
- Replacing the emulator would change `reference-system/` and the AppHost only.

## Deferred deliberately

- Lambda workloads, Kubernetes breadth and every scenario the admission rules do not yet admit.
- Deploying Sentinel itself into the emulator, and any production deployment definition; those belong to [Phase 09](phase-09-production-hardening.md).
- A CI pipeline for the conformance tests, which is Phase 00's open exit criterion and a prerequisite for Stage 1's isolated-runner rule.
- Multi-account and multi-region operation of Sentinel itself, which stays in [Phase 10](phase-10-scale-evolution.md); this phase only maps the fixture's accounts and regions onto tenants and environments.
