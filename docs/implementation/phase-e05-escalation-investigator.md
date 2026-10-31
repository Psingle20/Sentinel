# Phase E05 – Escalation investigator

Enhancement phase. Design source: [Model roles beyond the specialists](../architecture/model-roles.md), role 1, under [ADR-024](../decisions/ADR-024-model-roles.md) (accepted 2026-10-31). This document is the delivery view; the design holds the role's inputs, output, rules and evaluation measures.

## Outcome

When consensus would abstain because two explanations cannot be separated, Sentinel asks the `meta-wonder` profile for the one observation that would separate them, gathers it through an existing read-only tool, challenges the revised evidence and decides or abstains again. Cycles that end in escalation today end in a correct diagnosis where one observation was missing, and the role is kept only if the evaluation harness shows it introduces no wrong diagnosis.

## Primary service ownership

- `Sentinel.Runtime` owns the trigger: the consensus outcome that distinguishes ambiguity from absence of evidence, the once-per-cycle rule and the request for an escalation investigation through the existing `InvestigateIncident` command.
- `Sentinel.AgentWorker` owns the `escalation` role: the structured prompt, the `DiscriminatingQuery` contract validation, running the proposed tool call through the existing tools, and the finding it returns; plus the deterministic test double's escalation behaviour.
- `Sentinel.Contracts` gains the `escalation` role name, the `DiscriminatingQuery` shape inside the findings contract, and the hypothesis pair it separates.
- `scripts/evaluation` gains the escalation-yield measure and the role's investigator group.
- `Sentinel.Gateway` and `Sentinel.Web` change only to show the escalation as an investigation with its query and result, which the incident page's investigation list already does by role.
- `Sentinel.Executor` and `Sentinel.Ingestion` change nothing.

## Entry evidence required

- Consensus and the verifier are deterministic and tested, including the abstention outcomes ([Phase 05](phase-05-verification-consensus.md)).
- The `meta-wonder` profile exists with its own daily spend cap ([models and spend runbook](../runbooks/models-and-spend.md)).
- Every investigation records model, prompt version, tool calls and provider responses, so the new role is replayable ([Phase 07](phase-07-memory-replay-evaluation.md)).
- At least one scenario ends in abstention for ambiguity with the test double, or one is added to the reference system, so the role has a case to resolve.

## Slices

| Slice | Work | Evidence when done |
| --- | --- | --- |
| 1 – Trigger and contract | Consensus reports why it abstained (ambiguity versus missing evidence or falsification); the once-per-cycle escalation request; the `escalation` role and `DiscriminatingQuery` in Contracts with snapshots | Runtime tests for each abstention reason and for exactly one escalation per cycle; contract snapshot tests |
| 2 – The role with the test double | AgentWorker's escalation prompt over structured state only; validation that the proposed tool is in the catalogue and its range inside the incident window; the tool call run and recorded as a finding; the test double proposes the discriminating query for the ambiguous scenario | The ambiguous scenario is diagnosed correctly end to end with the test double; a query outside the catalogue or window is refused and recorded |
| 3 – Live profile and evaluation | The role routed to `meta-wonder` under its cap; the harness scores the `escalation` group and the escalation-yield measure; one recorded live run | Harness report with the measure; zero wrong diagnoses introduced on `S0` to `S10` |

## In scope

- The ambiguous scenario: either an existing scenario's package perturbed so one specialist's evidence is withheld, or a new reference scenario in which two services' symptoms coincide and one query separates them.
- The incident page and Judge mode showing the escalation as an investigation with its query, its result and the decision that followed.
- Runbook note under models and spend: what the escalation role costs and how to disable it by routing.

## Primary deliverables

1. The consensus abstention reasons and the once-per-cycle escalation request in Runtime.
2. The `escalation` role, `DiscriminatingQuery` contract and AgentWorker implementation, with the test double's behaviour.
3. The ambiguous scenario, its manifest and package.
4. The harness group and escalation-yield measure, and a recorded live run on `meta-wonder`.

## Test plan

- Runtime: abstention for ambiguity, for missing evidence and for falsification are distinguished; an escalation is requested only for ambiguity, only once, only when completeness is adequate and the budget allows; the second consensus uses the new finding.
- AgentWorker: the prompt carries structured state and no raw telemetry; a response naming an unknown tool, a widened range or a diagnosis is rejected; the tool call runs read-only and its result is a finding citing observations; the execution is replayable.
- Contracts: snapshot tests for the new role and shape.
- End to end: the ambiguous scenario with the test double, from abstention-without-escalation (role disabled) to correct diagnosis (role enabled), both recorded.
- Evaluation: the `escalation` group appears in the report with escalation yield; the gate still passes.

## Exit criteria

- The ambiguous scenario is diagnosed correctly with the escalation role and abstains without it, both recorded in the evaluation report.
- No scenario in the dataset gains a wrong diagnosis or a false remediation with the role enabled.
- The role runs at most once per cycle, only on ambiguity, only through catalogue tools inside the incident window, and every execution is replayable.
- A live run on `meta-wonder` is recorded with its cost, under the profile's cap.
- The incident page shows the escalation as an investigation; the ledger records the runs.

## Deferred deliberately

- A second escalation in the same cycle, or an escalation that proposes more than one query.
- Letting the escalation role see the second consensus or revise hypotheses itself.
- Any use of the role for remediation planning.
