# Phase 09 – Production hardening

## Outcome

Make the complete Sentinel loop secure, diagnosable and resilient under realistic faults, load bursts and dependency degradation.

## Primary service ownership

All five backend services and `Sentinel.Web`. Hardening is evaluated across their real network, data, credential and failure boundaries, including degraded operation when any one backend service is unavailable.

## In scope

- Timeouts, bounded retries with jitter, circuit breakers and bulkheads for every external dependency.
- Backpressure, quotas, rate limits, priority queues and load shedding.
- DLQ triage, replay authorization and poison-message quarantine.
- Idempotency across commands, projections, notifications and remediation.
- Leader election only where unavoidable; fencing for every privileged or singleton effect.
- Logical time/causality metadata sufficient for cross-service ordering; HLC only if measured requirements justify it.
- Sentinel self-observability: golden signals, workflow lag, actor health, projection lag, provider health and queue depth.
- AI observability: model latency, tokens, cost, schema failures, tool failures, quality and fallback rate.
- Security: identity, RBAC/ABAC as required, capability validation, secret rotation, encryption, audit integrity and dependency scanning.
- Prompt-injection and tool-safety regression suite.
- Privacy: minimization, redaction, retention, deletion, access logging and tenant isolation.
- Backup, restore, projection rebuild and disaster-recovery procedures.
- Capacity, performance and cost tests for the initial production envelope.
- Failure injection for provider, transport, database, workflow, Orleans node, model and remediation failures.
- Deployment health checks, safe rollout, rollback and operational runbooks.

## Failure-domain checklist

For every component document:

- owner and source of truth;
- timeout and retry budget;
- idempotency key and duplicate behavior;
- circuit-breaker and degraded mode;
- backpressure and capacity limit;
- crash/restart behavior;
- observability and alert;
- security boundary and credential scope;
- backup/recovery requirement;
- operator runbook.

## Primary deliverables

1. Service-level objectives for Sentinel itself.
2. Resilience policy library and dependency-specific configurations.
3. Security and privacy threat model with remediated findings.
4. Dashboards, alerts and trace exemplars for every critical flow.
5. Load, soak, chaos and recovery test suites.
6. Backup/restore and disaster-recovery evidence.
7. Production deployment definition, release checks and runbooks.

## Test plan

- Duplicate, out-of-order and delayed message storms.
- Provider, model and storage brownouts.
- Workflow-worker and Orleans-node termination mid-incident.
- Projection corruption followed by rebuild.
- Stale leader and duplicate remediation attempts.
- Tenant boundary and privilege-escalation tests.
- Malicious log/runbook prompt-injection fixtures.
- Load/soak tests across the full initial capacity envelope.
- Restore into an isolated environment and replay of an in-flight incident.

## Exit criteria

- No single external dependency failure causes uncontrolled cascading failure.
- All privileged effects are idempotent, fenced and fully audited.
- Target SLOs, capacity limits and degraded modes are documented and tested.
- Backup/restore and projection rebuild complete within accepted objectives.
- Security, privacy and prompt-injection test suites pass.
- Operators can diagnose and recover each critical failure using maintained runbooks.

## Deferred deliberately

Multi-region active/active operation, full cells, CRDT coordination and high-risk autonomous remediation remain Phase 10 decisions driven by measured product need.

