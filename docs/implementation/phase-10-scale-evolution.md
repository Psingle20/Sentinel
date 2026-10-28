# Phase 10 – Scale and architecture evolution

## Outcome

Evolve the validated product into a larger multi-tenant platform without rewriting its domain model or weakening safety. Every item in this phase requires measured demand and a separate architecture decision.

## Primary service ownership

All current deployables retain their accepted ownership. New services—including a replay worker—or splits and merges require measured evidence, an ADR, explicit data migration and rollback. Evolution expands the topology without dissolving trust boundaries.

## Entry evidence required

- Real workload measurements identify a bottleneck or failure-isolation problem.
- Tenant count, ingestion volume, incident concurrency and retention needs are known.
- Initial SLOs and cost envelopes have production evidence.
- Extraction or distribution provides a benefit that exceeds operational complexity.
- Security and data-residency requirements are explicit.

## Evolution workstreams

### Multi-tenancy and cells

- Tenant-aware quotas, noisy-neighbor controls and data placement.
- Cell-based partitioning by tenant/environment with bounded blast radius.
- Cell routing, placement, evacuation and capacity management.
- Per-cell observability, deployment and recovery procedures.

### Selective service evolution

The initial API, ingestion, runtime, agent-worker and executor boundaries may be split or merged only when independent scaling, ownership, release cadence or failure isolation is demonstrated. Replay/simulation is the leading candidate for an additional worker. Define API/event ownership and prohibit direct cross-service database access.

### Multi-region operation

- Residency and routing policy.
- Active/passive or active/active choice per data class.
- Regional failure and failover behavior.
- Replication conflict, recovery-point and recovery-time objectives.
- Control-plane consistency distinct from data-plane availability.

### Distributed coordination

- Partition ownership, leases, leader election and fencing.
- Hybrid logical clocks only where cross-region causal ordering requires them.
- CRDTs only for shared state that tolerates convergent semantics.
- No custom consensus algorithm or message broker.

### Advanced reasoning

- Hierarchical investigators for large incident graphs.
- Dynamic stream prioritization by impact and investigation need.
- Wider provider and knowledge ecosystem through conformance-tested adapters.
- Deeper simulation and digital-twin scenarios.
- Reliability learning from incident outcomes with guarded model/prompt rollout.

### Expanded autonomy

- Per-action evidence thresholds and safety cases.
- Shadow and suggestion-only evaluation before autonomous enablement.
- Progressive rollout by tenant, environment and action class.
- Continuous false-remediation monitoring and automatic autonomy rollback.
- High-risk actions remain human-controlled unless an explicit governance decision changes that rule.

## Primary deliverables

1. Measured scale model and capacity plan.
2. ADR per extraction, cell, region, CRDT/HLC or autonomy decision.
3. Migration plan preserving event/schema compatibility and rollback.
4. Tenant placement, quota and isolation controls.
5. Region/cell failure drills and operational dashboards.
6. Provider certification/conformance suite.
7. Safety evidence for every newly autonomous action class.

## Test plan

- Tenant noisy-neighbor and quota enforcement.
- Cell loss, evacuation and rebalancing.
- Regional partition, failover and reconciliation.
- Version-skew and rolling migration compatibility.
- Cross-service duplicate, ordering and contract tests.
- CRDT convergence tests only where CRDTs are adopted.
- Shadow comparison and canary rollback for reasoning/autonomy changes.

## Exit criteria

- Each distributed addition has measurable benefit and a documented operational owner.
- Cell or regional failure remains within the promised blast radius.
- Schema and event migrations preserve replay and auditability.
- Tenant isolation holds under load, failure and operator action.
- Expanded autonomy has statistically meaningful safety evidence and a rapid disable path.

## Explicit non-goals

- Technology adoption for appearance alone.
- A service per module.
- Custom databases, brokers, consensus protocols or tracing backends.
- Unrestricted shell access or production credentials for models.
- Full autonomy without policy, capability, audit and recovery verification.

