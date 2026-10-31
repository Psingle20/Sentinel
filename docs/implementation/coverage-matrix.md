# Architecture coverage matrix

This matrix maps every numbered section of the [Sentinel architecture](../architecture/architecture.md) to a primary implementation phase. Cross-cutting topics are revisited in later phases even when one phase owns the first implementation.

| Architecture section | Primary phase | Cross-cutting follow-up |
| --- | --- | --- |
| 1 Executive Summary | [00](phase-00-foundations.md) | All phases |
| 2 Core Product Vision | [00](phase-00-foundations.md) | [08](phase-08-product-experience.md), guided incident story in [E04](phase-e04-judge-mode.md) |
| 3 Architectural Principles | [00](phase-00-foundations.md) | All phases |
| 4 High-Level Architecture | [00](phase-00-foundations.md) | [09](phase-09-production-hardening.md), [10](phase-10-scale-evolution.md) |
| 5 Control Plane vs Data Plane | [08](phase-08-product-experience.md) | [03](phase-03-incident-runtime.md), [10](phase-10-scale-evolution.md) |
| 6 Domain Model | [00](phase-00-foundations.md) | [03](phase-03-incident-runtime.md), [06](phase-06-safe-remediation.md) |
| 7 Canonical Observation Model | [02](phase-02-telemetry-ingestion.md) | [07](phase-07-memory-replay-evaluation.md) |
| 8 Provider / Plugin Architecture | [02](phase-02-telemetry-ingestion.md) | [06](phase-06-safe-remediation.md), [10](phase-10-scale-evolution.md); AWS-shaped adapters in [E01](phase-e01-aws-reference-environment.md) |
| 9 Microsoft Orleans Model | [03](phase-03-incident-runtime.md) | [09](phase-09-production-hardening.md) |
| 10 Event Sourcing | [03](phase-03-incident-runtime.md) | [07](phase-07-memory-replay-evaluation.md) |
| 11 CQRS | [03](phase-03-incident-runtime.md) | [08](phase-08-product-experience.md) |
| 12 Durable Workflow Architecture | [03](phase-03-incident-runtime.md) | [06](phase-06-safe-remediation.md), [09](phase-09-production-hardening.md); engine per [ADR-005](../decisions/ADR-005-durable-workflow-engine.md) |
| 13 Saga / Compensation Model | [06](phase-06-safe-remediation.md) | [09](phase-09-production-hardening.md) |
| 14 Evidence Blackboard Architecture | [04](phase-04-ai-investigation.md) | [05](phase-05-verification-consensus.md) |
| 15 Evidence Model | [04](phase-04-ai-investigation.md) | [07](phase-07-memory-replay-evaluation.md), source-linked presentation in [E04](phase-e04-judge-mode.md) |
| 16 Causal Graph | [04](phase-04-ai-investigation.md) | [05](phase-05-verification-consensus.md), [08](phase-08-product-experience.md) |
| 17 Hypothesis Engine | [04](phase-04-ai-investigation.md) | [05](phase-05-verification-consensus.md) |
| 18 Adversarial Verification | [05](phase-05-verification-consensus.md) | [07](phase-07-memory-replay-evaluation.md) |
| 19 Multi-Agent Architecture | [04](phase-04-ai-investigation.md) | [05](phase-05-verification-consensus.md), [10](phase-10-scale-evolution.md) |
| 20 Agent Execution Contract | [04](phase-04-ai-investigation.md) | [09](phase-09-production-hardening.md) |
| 21 Consensus Engine | [05](phase-05-verification-consensus.md) | [07](phase-07-memory-replay-evaluation.md) |
| 22 Confidence Calibration | [05](phase-05-verification-consensus.md) | [07](phase-07-memory-replay-evaluation.md) |
| 23 Incident Correlation | [03](phase-03-incident-runtime.md) | [07](phase-07-memory-replay-evaluation.md) |
| 24 Event-Time Processing | [02](phase-02-telemetry-ingestion.md) | [03](phase-03-incident-runtime.md), [09](phase-09-production-hardening.md) |
| 25 SLO-Centric Incident Intelligence | [03](phase-03-incident-runtime.md) | [06](phase-06-safe-remediation.md), [08](phase-08-product-experience.md) |
| 26 Service Catalog | [03](phase-03-incident-runtime.md) | [08](phase-08-product-experience.md) |
| 27 Remediation Planning | [06](phase-06-safe-remediation.md) | [07](phase-07-memory-replay-evaluation.md) |
| 28 Policy-as-Code and Safety | [06](phase-06-safe-remediation.md) | [09](phase-09-production-hardening.md) |
| 29 Capability-Based Security | [06](phase-06-safe-remediation.md) | [09](phase-09-production-hardening.md) |
| 30 Human Approval | [06](phase-06-safe-remediation.md) | [08](phase-08-product-experience.md), the workspace's own approval path preserved in [E04](phase-e04-judge-mode.md) |
| 31 Recovery Verification | [06](phase-06-safe-remediation.md) | [07](phase-07-memory-replay-evaluation.md), recovery shown as the story's proof in [E04](phase-e04-judge-mode.md); Runtime-owned per [ADR-006](../decisions/ADR-006-remediation-control-path.md) |
| 32 Incident Memory | [07](phase-07-memory-replay-evaluation.md) | [10](phase-10-scale-evolution.md) |
| 33 Replay Architecture | [07](phase-07-memory-replay-evaluation.md) | [09](phase-09-production-hardening.md) |
| 34 Simulation / Digital Twin | [07](phase-07-memory-replay-evaluation.md) | [10](phase-10-scale-evolution.md); a second reference environment in [E01](phase-e01-aws-reference-environment.md) |
| 35 MAPE-K++ Autonomic Loop | [06](phase-06-safe-remediation.md) | [07](phase-07-memory-replay-evaluation.md), [10](phase-10-scale-evolution.md) |
| 36 Reliability and Resilience Patterns | [09](phase-09-production-hardening.md) | [02](phase-02-telemetry-ingestion.md), [03](phase-03-incident-runtime.md) |
| 37 Outbox / Inbox | [02](phase-02-telemetry-ingestion.md) | [03](phase-03-incident-runtime.md), [09](phase-09-production-hardening.md) |
| 38 Leader Election and Fencing | [09](phase-09-production-hardening.md) | [06](phase-06-safe-remediation.md), [10](phase-10-scale-evolution.md) |
| 39 Logical Time and Causality | [09](phase-09-production-hardening.md) | [10](phase-10-scale-evolution.md) |
| 40 CRDTs | [10](phase-10-scale-evolution.md) | Only with measured need |
| 41 Hierarchical Reasoning | [10](phase-10-scale-evolution.md) | [05](phase-05-verification-consensus.md) provides hooks |
| 42 Stream Prioritization | [02](phase-02-telemetry-ingestion.md) | [09](phase-09-production-hardening.md) |
| 43 Multi-Tenancy | [08](phase-08-product-experience.md) | [09](phase-09-production-hardening.md), [10](phase-10-scale-evolution.md) |
| 44 Cell-Based Architecture | [10](phase-10-scale-evolution.md) | Requires scale evidence |
| 45 Observability of Sentinel | [09](phase-09-production-hardening.md) | Baseline starts in [00](phase-00-foundations.md) |
| 46 AI Observability | [04](phase-04-ai-investigation.md) | [07](phase-07-memory-replay-evaluation.md), [09](phase-09-production-hardening.md) |
| 47 Auditability | [03](phase-03-incident-runtime.md) | [06](phase-06-safe-remediation.md), [09](phase-09-production-hardening.md) |
| 48 Data Privacy | [09](phase-09-production-hardening.md) | Controls begin in [02](phase-02-telemetry-ingestion.md) |
| 49 Prompt Injection and Tool Safety | [04](phase-04-ai-investigation.md) | [09](phase-09-production-hardening.md) |
| 50 Meta-Native Reference Deployment | [04](phase-04-ai-investigation.md) | [09](phase-09-production-hardening.md) |
| 51 Service Boundaries | [00](phase-00-foundations.md) | [10](phase-10-scale-evolution.md) |
| 52 Production-First Topology and Delivery Strategy | [00](phase-00-foundations.md) | [01A](phase-01a-walking-skeleton.md) delivers the first vertical feature; [10](phase-10-scale-evolution.md) |
| 53 Suggested Repository Structure | [00](phase-00-foundations.md) | Refined by concrete consumers |
| 54 Separation of Concerns by Project | [00](phase-00-foundations.md) | All implementation phases |
| 55 API Design Principles | [08](phase-08-product-experience.md) | Contracts begin in [00](phase-00-foundations.md) |
| 56 Async Messaging Model | [02](phase-02-telemetry-ingestion.md) | [03](phase-03-incident-runtime.md), [09](phase-09-production-hardening.md) |
| 57 Schema Versioning | [00](phase-00-foundations.md) | [02](phase-02-telemetry-ingestion.md), [07](phase-07-memory-replay-evaluation.md) |
| 58 Idempotency | [09](phase-09-production-hardening.md) | Required from first implementation in each phase |
| 59 Testing Strategy | [00](phase-00-foundations.md) | Every phase |
| 60 AI Evaluation Strategy | [07](phase-07-memory-replay-evaluation.md) | Scenarios and baseline defined in [01](phase-01-reference-system.md); metrics begin in [04](phase-04-ai-investigation.md); queue and AWS scenarios added by [E01](phase-e01-aws-reference-environment.md) |
| 61 Reference Incident Scenario | [01](phase-01-reference-system.md) | First end-to-end run in [01A](phase-01a-walking-skeleton.md); deepened through [08](phase-08-product-experience.md); extended by [E01](phase-e01-aws-reference-environment.md) |
| 62 Impact-Ordered Product Scope | [README](README.md) | All phases |
| 63 Non-Goals for MVP | [README](README.md) | Rechecked at each phase gate |
| 64 Architectural Decision Records | [00](phase-00-foundations.md) | Every consequential change |
| 65 OKT – Organized Knowledge Tree | [README](README.md) | Phase docs organize implementation views |
| 66 Recommended Per-Node OKT Template | [README](README.md) | Applied when component docs are created |
| 67 Suggested Implementation Order | [README](README.md) | Expanded into Phases 00–10, including 01A |
| 68 Architecture Review Questions | [README](README.md) | Used at every gate |
| 69 Product Narrative | [08](phase-08-product-experience.md) | Product framing only; no architecture dependency |
| 70 Final Architectural Position | [README](README.md) | Preserved across all phases |

## Completeness rule

A phase may split or move work when implementation evidence requires it, but this matrix must be updated in the same change. No architecture section may become unmapped. [Enhancement phases](README.md#enhancement-phases) appear only in the cross-cutting column; the primary phase of every section stays one of 00 to 10.

