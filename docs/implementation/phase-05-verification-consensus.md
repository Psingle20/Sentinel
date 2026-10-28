# Phase 05 – Adversarial verification and consensus

## Outcome

Turn competing hypotheses into an explainable decision by actively trying to falsify the leader, measuring evidence quality and preserving disagreement and uncertainty.

## Primary service ownership

`Sentinel.AgentWorker` performs adversarial investigation and returns structured challenges. `Sentinel.Runtime` owns evidence quality, consensus or abstention, and the immutable decision history. The worker never writes the authoritative decision directly.

## Vertical-slice position

Replaces the [walking skeleton's](phase-01a-walking-skeleton.md) single temporal-precedence check with adversarial verification and consensus. The end-to-end suite must pass with consensus on the path.

## In scope

- Adversarial verifier that searches for contradictory evidence and alternative causal explanations.
- Verification questions derived from the causal graph and hypothesis-specific falsifiers.
- Evidence independence analysis so duplicated or derivative signals are not counted repeatedly.
- Reliability-weighted consensus using evidence strength, source quality, agent history and calibration.
- Confidence updates that distinguish probability, evidence completeness and operational risk.
- Abstention when evidence is inadequate or contradictory.
- Consensus decision record containing the leader, alternatives, support, contradictions, dissent, confidence and missing evidence.
- `HypothesisGrain` and `ConsensusGrain` only where actor ownership improves concurrent updates.
- Hierarchical reasoning hooks for later incident subgraphs without requiring the full advanced design.
- Human-readable explanation generated from the structured decision record, not used as the source of truth.

## Decision flow

```text
Candidate hypotheses
  -> rank by current evidence
  -> define falsification tests
  -> collect targeted evidence
  -> update causal graph and confidence
  -> calculate consensus or abstain
  -> persist immutable decision record
```

## Primary deliverables

1. Challenge strategy and adversarial-agent contract.
2. Evidence-quality, independence and reliability scoring components.
3. Consensus algorithm with versioned inputs and reproducible outputs.
4. Decision record schema and history API.
5. Confidence calibration fixtures and reliability tracking.
6. Explanation view showing why alternatives lost.

## Required behaviors

- A high-confidence answer cannot be produced solely from one uncorroborated model statement.
- Contradictory evidence lowers confidence or triggers further investigation.
- Correlated copies of the same observation count as one evidence family.
- The system may abstain and request human investigation.
- A later observation can supersede a decision while preserving its history.

## Test and evaluation plan

- Known-cause fixtures with misleading correlated symptoms.
- Counterfactual fixtures where the recent deployment is not the root cause: the Phase 01 confounders `S2` and `S3`, plus fixtures derived from them.
- Duplicate-evidence and dependent-source tests.
- Disagreement, tie and low-evidence abstention tests.
- Calibration measurement across the growing replay suite.
- Golden scenario: connection leak wins only after competing explanations are challenged.

## Exit criteria

- The decision record identifies the leading causal chain and rejected alternatives.
- Supporting and contradicting evidence are inspectable and attributable.
- Replaying the same versioned evidence produces the same consensus result, excluding explicitly recorded model nondeterminism.
- The engine abstains below the configured evidence threshold.
- Confidence is not presented without its evidence completeness and calibration context.
- Sentinel beats the [deploy-blame baseline](phase-01-reference-system.md#non-ai-baseline) on `S1`–`S3`. It gives the correct diagnosis on `S1` and `S3`, never proposes a `checkout-service` rollback on `S2` (correct diagnosis or abstention), and makes zero false remediations.

## Deferred deliberately

Consensus proposes a diagnosis; it cannot authorize an action. Policy and execution remain separate in Phase 06.

