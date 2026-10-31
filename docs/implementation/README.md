# Sentinel implementation plan

This plan turns the [product architecture](../architecture/architecture.md) into independently reviewable delivery phases. Phases are ordered by product impact and dependency, not by an assumed calendar. A phase is complete only when its exit criteria pass; unfinished work is carried explicitly rather than hidden in a later phase.

## Delivery strategy

The first objective is one credible closed loop:

```text
Observe -> Correlate -> Investigate -> Challenge -> Decide
        -> Approve -> Act -> Verify -> Learn
```

Sentinel uses the production [five-service topology](production-topology.md) from the start: API, ingestion, runtime, agent worker, and executor, plus one web frontend. Provider SDKs, Meta model access, Orleans, storage, and transport remain behind Sentinel-owned contracts.

> **Non-negotiable:** Build one vertical feature through all five backend services before expanding any individual service.

[Phase 01A](phase-01a-walking-skeleton.md) delivers that feature as a walking skeleton: the complete golden path across every service, with the simplest honest implementation at each step. Phases 02–08 each deepen one part of the path, replace the skeleton stubs named in Phase 01A, and keep the path running end to end. Later phases retain every planned capability through the [coverage matrix](coverage-matrix.md).

The [Phase 01 scenario suite](phase-01-reference-system.md#scenario-suite) includes confounders where the most recent deployment is not the cause, and a deterministic [deploy-blame baseline](phase-01-reference-system.md#non-ai-baseline) that Sentinel's reasoning must beat.

The frontend follows the [framework-first design-system plan](../architecture/frontend-design-system.md). Phase 00 establishes tokens, approved components, Storybook and enforcement before feature screens expand; Phase 08 completes the responder and administrator patterns.

## Phases

| Phase | Outcome | Depends on |
| --- | --- | --- |
| [00 – Foundations](phase-00-foundations.md) | Buildable skeleton, stable contracts, engineering guardrails | None |
| [01 – Reference system](phase-01-reference-system.md) | Controlled distributed failures, confounders and baseline with deterministic reset | 00 |
| [01A – Walking skeleton](phase-01a-walking-skeleton.md) | The complete golden path runs once across all five services and the web frontend | 00, 01 |
| [02 – Telemetry ingestion](phase-02-telemetry-ingestion.md) | Canonical logs, metrics, traces, changes, topology and SLO signals | 01A |
| [03 – Incident runtime](phase-03-incident-runtime.md) | Correlated, durable, queryable incident lifecycle | 02 |
| [04 – AI investigation](phase-04-ai-investigation.md) | Meta-powered specialists produce attributable evidence and hypotheses | 03 |
| [05 – Verification and consensus](phase-05-verification-consensus.md) | Leading explanations are challenged and resolved transparently | 04 |
| [06 – Safe remediation](phase-06-safe-remediation.md) | Policy-gated action followed by measured recovery | 03, 05 |
| [07 – Memory, replay and evaluation](phase-07-memory-replay-evaluation.md) | Reproducible incidents, postmortems and AI quality benchmarks | 03–06 |
| [08 – Product experience](phase-08-product-experience.md) | Usable control plane, API and responder workflow | 03–07 |
| [09 – Production hardening](phase-09-production-hardening.md) | Secure, observable and failure-tolerant operation | 00–08 |
| [10 – Scale and evolution](phase-10-scale-evolution.md) | Multi-tenant scale, cells, selective extraction and advanced autonomy | 09 plus measured need |

The [phase status ledger](phase-status.md) records each phase's status, the evidence for what it has delivered and its open exit criteria. Update it in the same change that moves a phase.

## Enhancement phases

Phases 00 to 10 implement the architecture document, and the [coverage matrix](coverage-matrix.md) is their completeness check. Enhancement phases, numbered `E01` onward, add capability the architecture permits but does not require: a new reference environment, a provider family, a scenario class, an integration. One is created when a design proposal under `docs/architecture/` is ready to be delivered, and more follow as features expand. Every enhancement phase:

- starts from a Proposed design document under `docs/architecture/`, which it links as its design source and does not repeat; the phase document is the delivery view (outcome, ownership, entry evidence, workstreams, test plan, exit criteria, deferrals);
- names the decision records it needs and gets them before implementation creates lock-in;
- obeys the [universal definition of done](#universal-definition-of-done) and the [planning rules](#planning-rules), including the five-service boundary and the end-to-end suite;
- enters the [phase status ledger](phase-status.md) when it is created and becomes Complete only when its exit criteria have evidence there;
- appears in the coverage matrix's cross-cutting column where it touches an architecture section, but is never the primary phase for one.

| Phase | Outcome | Design source | Depends on |
| --- | --- | --- | --- |
| [E01 – AWS reference environment](phase-e01-aws-reference-environment.md) | Non-deployment causes and non-rollback remedies, first with a queue in the existing reference system, then in the AWS environment behind its compatibility gate | [Proposal](../architecture/aws-reference-environment.md) | 01, 06, 07 complete; ADR-011 amendment and ADR-021 |
| [E02 – Frontend design-system depth](phase-e02-frontend-design-system-depth.md) | The design system covers every form, message and layout the product renders, with the full token model, theme selection and tablet coverage | [Design-system plan and gap assessment](../architecture/frontend-design-system.md#gap-assessment-2026-10-30) | Phase 00 design-system foundation; the responder and administrator journeys |
| [E03 – Demonstration depth](phase-e03-demonstration-depth.md) | Sentinel's judgement shown live: an instruction planted in its data shown and ignored, an incomplete fix corrected, a repeat recognised, the damage avoided beside each incident and investigations watched as they happen | [Demonstration depth](../architecture/demonstration-depth.md) | 05, 06, 07 complete |
| [E04 – Judge mode](phase-e04-judge-mode.md) | One incident shown as a guided, source-linked story from context to proven recovery, in captured mode from a run record and in live mode from the existing stream, built only on records the Gateway already serves | [Judge mode](../architecture/judge-mode.md) | 08's incident page and stream; E03's `S9` and `S10`; E02's primitives |

## Universal definition of done

Every phase must:

- preserve tenant, environment, service and incident identity across all contracts;
- treat telemetry, documents and model output as untrusted data;
- preserve evidence provenance and an immutable decision trail;
- define ownership, source of truth, retry behavior and idempotency;
- add relevant unit, integration, replay and failure tests beside the owning code;
- emit OpenTelemetry signals for critical paths and external calls;
- document new APIs, events, schemas, operational procedures and consequential decisions;
- preserve the five accepted service ownership boundaries and require an ADR for any additional deployable;
- from Phase 01A onward, keep the walking-skeleton end-to-end suite passing with the phase's implementation on the path;
- pass all commands declared in `PROJECT.json` that apply at that point.

## Planning rules

- Optimize for a working end-to-end path before breadth.
- Do not expand one service while the selected vertical slice is incomplete in another.
- A deepening phase replaces skeleton stubs behind their existing contracts; contract changes are versioned and documented.
- Frontend work must compose approved design-system components and semantic tokens; new visual conventions require design-system review.
- Use feature flags or adapters to isolate incomplete integrations.
- Keep high-risk actions in suggestion-only mode until Phase 06 gates pass.
- Prefer representative fixtures over many shallow provider integrations.
- Record new architecture decisions under `docs/decisions/` before implementation creates lock-in.
- Revisit phase ordering when evidence changes, but keep the [coverage matrix](coverage-matrix.md) complete.

## Architecture coverage

The [coverage matrix](coverage-matrix.md) maps every major section of the architecture to a primary phase and identifies cross-cutting phases. It is the completeness check for this plan.

The [production topology and vertical-slice plan](production-topology.md) maps every phase to its owning deployables and defines the cross-service acceptance path.

## Decisions that shape the plan

- [ADR-001](../decisions/ADR-001-production-first-service-topology.md): five-service topology and the vertical-slice rule.
- [ADR-003](../decisions/ADR-003-dotnet-backend-platform.md): .NET 10 LTS backend platform; Python only for offline tooling.
- [ADR-004](../decisions/ADR-004-runtime-state-ownership.md): the event stream is authoritative; the workflow owns progress only.
- [ADR-005](../decisions/ADR-005-durable-workflow-engine.md): Temporal for durable workflows, never the source of truth.
- [ADR-006](../decisions/ADR-006-remediation-control-path.md): approval, execution and recovery control path, including Executor's execution state machine.
- [ADR-007](../decisions/ADR-007-cross-service-code-and-provider-access.md): no shared domain or interface packages; provider access only through Ingestion, with freshness metadata.

## Foundation evolution

The [Clean Architecture Building Blocks evolution plan](building-blocks-evolution.md) defines how the existing reusable package is adopted during Phase 00, separated into stable layers, hardened through two real consumers and eventually extracted into its own versioned project.
