# ADR-011: Reference system location

- Status: Accepted
- Date: 2026-10-28

## Context

The Phase 01 reference system (checkout service, order service, reporting worker, Redis and a deploy controller) is executable code, but it is not one of Sentinel's five services. It is the system Sentinel observes and remediates, and the permanent end-to-end test fixture. Project policy requires documenting any project-specific top-level directory.

## Considered options

1. A top-level `reference-system/` directory inside the Sentinel project.
2. A separate workspace project under `projects/`.
3. A directory under `deploy/`.

## Decision

Use a top-level **`reference-system/`** directory in the Sentinel project:

```text
reference-system/
├── services/            checkout-service, order-service, reporting-worker
├── deploy-controller/   deploys healthy or faulty versions; target of the rollback adapter
├── scenarios/           S0–S3 manifests and the deploy-blame baseline definition
└── deploy/              its Redis and observability stack configuration
```

- Sentinel code never references reference-system code. Sentinel interacts with it only as it would with a real system: through telemetry providers and the deploy controller's API.
- Failure-control endpoints are disabled outside explicitly marked development and test environments (Phase 01).
- The reference system has its own Redis, separate from Sentinel's messaging Redis ([ADR-008](ADR-008-event-transport.md)).

Option 2 would make Sentinel's end-to-end suite depend on another project. Option 3 would mix application source with deployment configuration.

## Consequences

### Positive

- The fixture is versioned with the scenarios and tests that depend on it.
- The boundary is visible: nothing in `services/` may reference `reference-system/`.

### Negative

- The Sentinel project contains code that is not part of the product; architecture tests must enforce the separation.

## Revisit criteria

Revisit if the reference system grows into a reusable fixture for other projects.
