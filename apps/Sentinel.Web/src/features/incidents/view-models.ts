import type { ComparedHypothesis, HypothesisStanding } from "@/design-system/patterns/hypothesis-comparison";
import type { AgentRun } from "@/design-system/patterns/agent-activity";
import type { GraphEdge, GraphNode } from "@/design-system/patterns/evidence-graph";
import type { ReviewedPlan } from "@/design-system/patterns/remediation-review";
import type { SourcedFact } from "@/design-system/patterns/fact-list";
import type { AgentExecution, HypothesisView, IncidentDetail, PlanView, PostmortemFact } from "@/lib/api/contracts";

import { approvalStatus, describeAction, describeCriterion, formatTime, policyVerdict } from "./presentation";

// Maps Api's contracts onto the design system's product patterns. Pure functions, so every
// visible conclusion is derived from the structured records in one testable place.

function hypothesisFor(incident: IncidentDetail, id: string | undefined): HypothesisView | undefined {
  if (!id) {
    return undefined;
  }
  return (incident.hypotheses ?? []).find((hypothesis) => hypothesis.finding.hypothesisId === id || (hypothesis.lineage ?? []).includes(id));
}

export function leadingHypothesis(incident: IncidentDetail): HypothesisView | undefined {
  return hypothesisFor(incident, incident.decision?.leadingHypothesisId);
}

export function graphOf(incident: IncidentDetail): { nodes: GraphNode[]; edges: GraphEdge[]; leadingIds: string[] } {
  const graph = incident.causalGraph ?? { nodes: [], edges: [] };
  const leading = leadingHypothesis(incident);
  return {
    nodes: graph.nodes.map((node) => ({ id: node.id, kind: node.kind, label: node.label })),
    edges: graph.edges.map((edge) => ({ from: edge.from, to: edge.to, relation: edge.relation })),
    leadingIds: leading ? [`hypothesis:${leading.finding.hypothesisId}`] : [],
  };
}

export function comparedHypotheses(incident: IncidentDetail): ComparedHypothesis[] {
  const decision = incident.decision;
  const assessments = decision?.assessments ?? [];
  const dissent = new Set(decision?.dissentingHypothesisIds ?? []);
  const leading = leadingHypothesis(incident);

  return (incident.hypotheses ?? [])
    .map((hypothesis) => {
      const ids = new Set([hypothesis.finding.hypothesisId, ...(hypothesis.lineage ?? [])]);
      const assessment = assessments.find((item) => ids.has(item.hypothesisId));
      let standing: HypothesisStanding = "standing";
      if (leading && leading.finding.hypothesisId === hypothesis.finding.hypothesisId) {
        standing = "leading";
      } else if (assessment?.eliminated || hypothesis.status === "rejected") {
        standing = "eliminated";
      } else if ([...ids].some((id) => dissent.has(id))) {
        standing = "dissent";
      }

      return {
        id: hypothesis.finding.hypothesisId,
        statement: hypothesis.finding.statement,
        hypothesisKey: hypothesis.finding.key,
        service: hypothesis.finding.suspectedServiceId,
        standing,
        score: assessment?.score,
        support: assessment?.independentSupport,
        contradiction: assessment?.independentContradiction,
        reason: assessment?.reason,
        challenges: (incident.challenges ?? [])
          .filter((challenge) => ids.has(challenge.hypothesisId))
          .map((challenge) => ({ falsifier: challenge.falsifier, verdict: challenge.verdict, detail: challenge.detail })),
      };
    })
    .sort((left, right) => order(left.standing) - order(right.standing) || (right.score ?? 0) - (left.score ?? 0));
}

function order(standing: HypothesisStanding): number {
  return { leading: 0, dissent: 1, standing: 2, eliminated: 3 }[standing];
}

export function reviewedPlan(view: PlanView): ReviewedPlan {
  const { plan, policyEvaluation } = view;
  return {
    version: plan.planVersion,
    actions: plan.actions.map(describeAction),
    risk: plan.risk,
    blastRadius: plan.blastRadius ?? [],
    preconditions: (plan.preconditions ?? []).map((precondition) => `${precondition.serviceId} ${precondition.check} is ${precondition.expected}`),
    expectedEffect: plan.expectedEffect,
    expectedRecovery: plan.expectedRecovery.map(describeCriterion),
    observationWindow: plan.observationWindow,
    compensation: plan.compensation,
    policy: { version: policyEvaluation.policyVersion, decision: policyVerdict(policyEvaluation.decision), trace: policyEvaluation.ruleTrace },
    planHash: plan.planHash,
    approval: approvalStatus(view),
    approvedBy: view.approvedBy,
  };
}

function elapsedSeconds(start: string, end: string): number {
  return (new Date(end).getTime() - new Date(start).getTime()) / 1000;
}

export function agentRuns(executions: AgentExecution[], incident: IncidentDetail): AgentRun[] {
  const roles = new Map((incident.investigations ?? []).map((investigation) => [investigation.investigationId, investigation.role]));
  return executions.map((execution) => ({
    id: execution.agentExecutionId,
    investigator: execution.investigator,
    role: execution.shadow ? "deploy-blame baseline" : (roles.get(execution.investigationId) ?? undefined),
    shadow: execution.shadow,
    model: `${execution.modelProvider}/${execution.model}`,
    promptVersion: execution.promptVersion,
    result: execution.result,
    toolCalls: execution.toolCalls?.length ?? 0,
    failedToolCalls: (execution.toolCalls ?? []).filter((call) => call.error).length,
    tokens: execution.inputTokens + execution.outputTokens,
    durationSeconds: elapsedSeconds(execution.startedAt, execution.completedAt),
    note: execution.abstentionReason,
  }));
}

// Sources link into the incident page where they can: evidence rows have anchors.
export function sourcedFacts(facts: PostmortemFact[] | undefined, incidentId: string): SourcedFact[] {
  return (facts ?? []).map((fact) => ({
    text: fact.text,
    at: fact.at ? formatTime(fact.at) : undefined,
    sources: fact.sources.map((source) => ({
      kind: source.kind,
      ref: source.ref,
      href: source.kind === "evidence" ? `/incidents/${encodeURIComponent(incidentId)}#evidence-${source.ref}` : undefined,
    })),
  }));
}
