# Phase 08 – Product experience, API and control plane

## Outcome

Give incident responders a coherent operational product for understanding, controlling and reviewing the complete Sentinel loop, while giving administrators safe control-plane configuration.

## Primary service ownership

`Sentinel.Web` owns the responder experience and `Sentinel.Gateway` owns its authenticated API and live-update boundary. All five backend services publish authoritative status through contracts; the frontend never calls their databases or internal endpoints directly.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) polling incident pages and local development identities with the full responder experience, live updates and real identity. Approval continues to go from Web to Api to Runtime ([ADR-006](../decisions/ADR-006-remediation-control-path.md)). The end-to-end suite must pass through the new UI.

## In scope

- Complete the [frontend design system](../architecture/frontend-design-system.md) without introducing a competing CSS or component framework.
- Expand approved semantic tokens, domain components and product patterns only for concrete responder and administrator journeys.
- Responder experience for incident list, live timeline, affected services, SLO impact and current workflow phase.
- Evidence graph with source links, supporting/contradicting direction and causal relationships.
- Hypothesis comparison showing confidence, missing evidence, challenge results, dissent and abstention.
- Root-cause decision card generated from the structured consensus record.
- Remediation-plan review with risk, blast radius, preconditions, rollback and expected recovery signals.
- Approval, rejection, expiry, emergency stop and manual override interactions.
- Execution and recovery views with before/after telemetry.
- Agent activity view showing tools, status, latency and bounded reasoning metadata without exposing secrets.
- Replay and postmortem entry points.
- Domain-oriented APIs for investigations, challenges, remediation, approval, execution, evidence graph and timeline.
- Control-plane administration for tenants, environments, services, owners, SLOs, providers, model selection, policy, notifications, features and workflow configuration.
- Role-based access and tenant scoping across UI and APIs.
- Notification routing through provider abstraction.

## Primary user journeys

1. Responder opens a newly correlated incident and understands impact.
2. Responder watches specialists collect evidence and compare hypotheses.
3. Responder inspects why the leading explanation survived challenge.
4. Approver reviews a versioned remediation plan and grants or denies it.
5. Responder watches execution and measured recovery.
6. Responder replays the incident and exports/reviews the postmortem.
7. Administrator configures a provider, SLO, policy and model without editing code.

## API principles

- Model domain actions, not database tables.
- Require idempotency keys for mutations.
- Use explicit versions and optimistic concurrency for mutable resources.
- Paginate potentially large timelines and evidence sets.
- Return stable problem details and correlation identifiers.
- Stream live updates through a resumable mechanism; retain query APIs as the source of truth.
- Separate control-plane authorization from data-plane incident permissions.

## Primary deliverables

1. Responder web application built entirely from the governed Tailwind, shadcn/ui, Radix UI and Sentinel-token design system established in Phase 00.
2. API/edge layer and generated or maintained contract documentation.
3. Live-update channel with reconnect and catch-up behavior.
4. Control-plane configuration workflows and audit history.
5. Notification adapter and routing rules.
6. Product analytics limited to approved, privacy-safe events.

## Test plan

- End-to-end tests for every primary journey.
- Authorization tests by role, tenant, environment and incident.
- Stale-plan, concurrent-approval and reconnect tests.
- Accessibility and keyboard-navigation checks.
- Storybook interaction, automated accessibility and visual-regression checks for every reusable component and operational state.
- Static checks for unauthorized UI frameworks, arbitrary raw values, inline styling and undocumented custom CSS.
- Large timeline/evidence graph performance tests.
- Contract compatibility and API idempotency tests.

## Exit criteria

- A responder can complete the golden scenario without database or terminal access.
- Every visible conclusion links to its structured evidence and decision record.
- Approval always references the exact immutable plan version.
- The UI recovers from disconnects without losing authoritative state.
- Control-plane changes are validated, authorized and audited.
- The complete golden journey uses approved components and semantic tokens without page-specific visual conventions.

## Deferred deliberately

Advanced customization and broad notification/provider catalogs wait until core workflows are validated by real users.

