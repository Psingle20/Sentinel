import type { ComparedHypothesis, HypothesisStanding } from "@/design-system/patterns/hypothesis-comparison";
import type { AgentRun } from "@/design-system/patterns/agent-activity";
import type { GraphEdge, GraphNode } from "@/design-system/patterns/evidence-graph";
import type { ReviewedPlan } from "@/design-system/patterns/remediation-review";
import type { SourcedFact } from "@/design-system/patterns/fact-list";
import type { AgentExecution, HypothesisFinding, HypothesisView, IncidentDetail, InvestigationView, PlanView, PostmortemFact, SimilarIncident } from "@/lib/api/contracts";

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

// Phase E05: the latest escalation investigation, when consensus asked one more question before deciding.
export function escalationOf(incident: IncidentDetail): InvestigationView | undefined {
  return (incident.investigations ?? [])
    .filter((investigation) => investigation.role === "escalation")
    .sort((left, right) => right.requestedAt.localeCompare(left.requestedAt))[0];
}

// Phase E05: the statements of the two explanations an ambiguous abstention could not choose between, the leader first.
export function contenders(incident: IncidentDetail): string[] {
  return (incident.decision?.contenders ?? [])
    .map((id) => hypothesisFor(incident, id)?.finding.statement)
    .filter((statement): statement is string => Boolean(statement));
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

// Phase E03: a remembered incident of the tenant that resolved and resembles this one strongly enough to call it a repeat.
// Still a hint: the page cites it, and nothing Sentinel concludes rests on it.
const seenBeforeSimilarity = 0.6;

export function seenBefore(similar: SimilarIncident[] | null, incidentId: string): SimilarIncident | undefined {
  return (similar ?? [])
    .filter((match) => match.incidentId !== incidentId && match.outcome === "resolved" && match.similarity >= seenBeforeSimilarity)
    .sort((left, right) => right.similarity - left.similarity)[0];
}

export type CounterfactualVerdict = "agrees" | "rejected" | "outranked" | "unexamined" | "abstained";

export type Counterfactual = {
  verdict: CounterfactualVerdict;
  // What the baseline would have rolled back, for example "checkout-service v2.4.0 to v2.4.1".
  target?: string;
  // Why Sentinel set that explanation aside, when it did.
  reason?: string;
  // What Sentinel proposes instead.
  instead?: string;
};

// Phase E03: what the deploy-blame baseline would have done, from its latest shadow run, set beside Sentinel's own
// verdict on the same change. AgentWorker records the shadow findings with the incident's agent executions; Runtime
// never receives them.
export function counterfactual(executions: AgentExecution[] | null, incident: IncidentDetail): Counterfactual | undefined {
  const shadow = (executions ?? [])
    .filter((execution) => execution.shadow && execution.findings)
    .sort((left, right) => right.startedAt.localeCompare(left.startedAt))[0];
  if (!shadow?.findings || !incident.decision) {
    return undefined;
  }

  let findings: { outcome?: string; hypotheses?: HypothesisFinding[] };
  try {
    findings = JSON.parse(shadow.findings) as typeof findings;
  } catch {
    return undefined;
  }

  const leading = leadingHypothesis(incident);
  const instead = incident.plan ? incident.plan.plan.actions.map(describeAction).join("; ") : leading?.finding.statement;
  const blamed = findings.outcome === "completed" ? findings.hypotheses?.find((hypothesis) => hypothesis.suspectedChangeObservationId) : undefined;
  if (!blamed?.suspectedChangeObservationId) {
    return { verdict: "abstained", instead };
  }

  const changeId = blamed.suspectedChangeObservationId;
  const change = (incident.changes ?? []).find((linked) => linked.observationIds.includes(changeId));
  const named = /^The (\S+) (\S+) deployment/.exec(blamed.statement);
  let target = blamed.suspectedServiceId ?? "the latest deployment";
  if (change) {
    target = `${change.serviceId} ${change.version}${change.previousVersion ? ` to ${change.previousVersion}` : ""}`;
  } else if (named) {
    target = `${named[1]} ${named[2]}`;
  }

  if (leading?.finding.suspectedChangeObservationId === changeId) {
    return { verdict: "agrees", target, instead };
  }

  const sentinel = (incident.hypotheses ?? []).find((hypothesis) => hypothesis.finding.suspectedChangeObservationId === changeId);
  if (!sentinel) {
    return { verdict: "unexamined", target, instead };
  }

  const compared = comparedHypotheses(incident).find((hypothesis) => hypothesis.id === sentinel.finding.hypothesisId);
  const falsified = compared?.challenges.find((challenge) => challenge.verdict === "falsified");
  return {
    verdict: compared?.standing === "eliminated" ? "rejected" : "outranked",
    target,
    reason: falsified?.detail ?? compared?.reason,
    instead,
  };
}

export type InvestigationStepState = "waiting" | "reported" | "abstained" | "failed" | "timed-out";

export type InvestigationStep = {
  role: string;
  state: InvestigationStepState;
  requestedAt: string;
  completedAt?: string;
  evidence: number;
  hypotheses: number;
};

// Phase E03: the current cycle's investigations as they progress, the specialists first and the verifier last. The page
// refreshes on every live event, so this shows each report as it arrives. A role asked again in a later cycle shows its
// latest request only.
export function investigationProgress(incident: IncidentDetail): InvestigationStep[] {
  const latest = new Map<string, NonNullable<IncidentDetail["investigations"]>[number]>();
  for (const investigation of incident.investigations ?? []) {
    const role = investigation.role ?? "generalist";
    const current = latest.get(role);
    if (!current || current.requestedAt <= investigation.requestedAt) {
      latest.set(role, investigation);
    }
  }

  return [...latest.entries()]
    .map(([role, investigation]) => {
      let state: InvestigationStepState = "waiting";
      if (investigation.timedOut) {
        state = "timed-out";
      } else if (investigation.outcome === "completed") {
        state = "reported";
      } else if (investigation.outcome) {
        state = investigation.outcome;
      }

      return {
        role,
        state,
        requestedAt: investigation.requestedAt,
        completedAt: investigation.completedAt,
        evidence: (incident.evidence ?? []).filter((item) => item.investigationId === investigation.investigationId && item.status === "accepted").length,
        hypotheses: (incident.hypotheses ?? []).filter((hypothesis) => (hypothesis.proposedBy ?? [hypothesis.investigationId]).includes(investigation.investigationId)).length,
      };
    })
    .sort((left, right) => Number(left.role === "verifier") - Number(right.role === "verifier") || left.requestedAt.localeCompare(right.requestedAt) || left.role.localeCompare(right.role));
}
