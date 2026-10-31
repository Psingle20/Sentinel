import type { SourcedFact } from "@/design-system/patterns/fact-list";
import type { AgentExecution, IncidentDetail, IncidentRunRecord, IncidentStatus, RecoveryOutcome } from "@/lib/api/contracts";
import { describeAction, describeCriterion, executionLabel, formatTime, percent, policyVerdict } from "@/features/incidents/presentation";
import { comparedHypotheses, contenders, counterfactual, investigationProgress, leadingHypothesis } from "@/features/incidents/view-models";

// Phase E04, Judge mode: one incident told as a guided story from context to proven recovery. Every stage is a pure
// function of the records the Gateway already serves. The view selects, orders and summarizes; it owns no fact and
// computes no verdict. Summaries come from these versioned templates, so a captured run plays the same way every time.
export const storyTemplates = "judge-v1";

// The run-record schema versions Judge mode can play; any other is refused rather than guessed at. Version 2 added the
// version manifest and the memory record, which a version 1 record simply lacks.
export const playableRunRecords = [1, 2];

export type StoryMode = "live" | "captured";

// A stage appears only once its source exists, so none is ever pending: it is in progress, complete, failed or inconclusive.
export type StageStatus = "active" | "complete" | "failed" | "inconclusive";

export type StageId =
  | "context"
  | "detection"
  | "correction"
  | "investigation"
  | "manipulation"
  | "hypothesis"
  | "challenge"
  | "decision"
  | "counterfactual"
  | "governance"
  | "action"
  | "recovery";

export type Stage = {
  id: StageId;
  title: string;
  question: string;
  status: StageStatus;
  // The view's own one-paragraph summary of the facts below it, from the templates above.
  summary: string;
  // When the stage's records say it happened, as recorded; playback never changes it.
  at?: string;
  // Recorded facts, each naming the records it comes from.
  facts: SourcedFact[];
  // Where the full record is in the incident workspace.
  anchor: string;
};

export type StoryOutcome = {
  incident: IncidentStatus;
  action: string;
  // Undefined until Sentinel has judged recovery.
  recovery?: RecoveryOutcome;
  next: string;
};

export type Story = {
  incidentId: string;
  stages: Stage[];
  outcome: StoryOutcome;
  // False when AgentWorker's execution records could not be read: model detail is then marked unavailable, never inferred.
  executionsAvailable: boolean;
  // The model providers the investigation used, as its executions record them.
  providers: string[];
};

type Records = { incident: IncidentDetail; executions: AgentExecution[] | null };

function workspace(incidentId: string, anchor: string): string {
  return `/incidents/${encodeURIComponent(incidentId)}${anchor}`;
}

function plural(count: number, one: string, many = `${one}s`): string {
  return `${count} ${count === 1 ? one : many}`;
}

function context({ incident }: Records): Stage {
  const { summary } = incident;
  const affected = incident.affectedServices?.length ? incident.affectedServices : [summary.affectedServiceId];
  const slo = (incident.linkedSignals ?? []).find((signal) => signal.metricName.includes("slo"));
  const href = workspace(summary.incidentId, "");
  return {
    id: "context",
    title: "Context",
    question: "What is at risk?",
    status: "complete",
    summary: `${summary.title}. ${summary.severity[0].toUpperCase()}${summary.severity.slice(1)} severity, affecting ${affected.join(", ")}${slo ? `; ${slo.serviceId}'s ${slo.metricName} reached ${slo.value}` : ""}.`,
    at: summary.openedAt,
    facts: [
      { text: `Severity ${summary.severity}, opened ${formatTime(summary.openedAt)}`, at: formatTime(summary.openedAt), sources: [{ kind: "incident", ref: summary.incidentId, href }] },
      ...affected.map((service) => ({ text: `${service} is affected`, sources: [{ kind: "incident", ref: summary.incidentId, href }] })),
    ],
    anchor: "",
  };
}

function detection({ incident }: Records): Stage | undefined {
  const triggers = incident.triggerObservationIds ?? [];
  const opened = (incident.timeline ?? [])[0];
  if (triggers.length === 0 && !opened) {
    return undefined;
  }

  const changes = incident.changes ?? [];
  const timeline = workspace(incident.summary.incidentId, "#timeline-title");
  return {
    id: "detection",
    title: "Detection",
    question: "What opened the incident?",
    status: "complete",
    summary:
      `Sentinel opened the incident from ${plural(triggers.length, "trigger observation")}` +
      (changes.length ? `, with ${plural(changes.length, "deployment")} in its window: ${changes.map((change) => `${change.serviceId} ${change.version}`).join(", ")}.` : ", with no deployment in its window."),
    at: opened?.occurredAt ?? incident.summary.openedAt,
    facts: [
      ...(opened ? [{ text: opened.summary, at: formatTime(opened.occurredAt), sources: [{ kind: "event", ref: String(opened.streamVersion), href: timeline }] }] : []),
      ...triggers.map((id) => ({ text: "Trigger observation", sources: [{ kind: "observation", ref: id }] })),
      ...changes.map((change) => ({
        text: `${change.serviceId} ${change.version}${change.previousVersion ? ` replaced ${change.previousVersion}` : ""}`,
        at: change.startedAt ? formatTime(change.startedAt) : undefined,
        sources: change.observationIds.map((id) => ({ kind: "observation", ref: id })),
      })),
    ],
    anchor: "#timeline-title",
  };
}

// Phase E03's follow-up cycle: a remedy that took effect without recovering the incident, and the second look it led to.
function correction({ incident }: Records): Stage | undefined {
  const tried = incident.remediesTried ?? [];
  if (tried.length === 0) {
    return undefined;
  }

  const first = tried[0];
  const followUp = (incident.timeline ?? []).find((entry) => entry.summary.startsWith("Follow-up cycle:"));
  const execution = workspace(incident.summary.incidentId, "#execution-title");
  return {
    id: "correction",
    title: "Self-correction",
    question: "Did the first remedy end the incident?",
    status: "complete",
    summary:
      `The first remedy, ${first.summary}, took effect at its target, but recovery was ${first.outcome}. ` +
      (first.followedUp
        ? `Sentinel investigated again in cycle ${first.cycle + 2}, knowing what it had tried; the stages after this one are that cycle's.`
        : "Sentinel escalated to responders instead of trying again."),
    at: first.judgedAt,
    facts: [
      ...tried.map((remedy) => ({
        text: `Cycle ${remedy.cycle + 1}: ${remedy.summary}; recovery ${remedy.outcome}${remedy.reason ? `: ${remedy.reason}` : ""}`,
        at: formatTime(remedy.judgedAt),
        sources: [{ kind: "plan", ref: remedy.planId, href: execution }],
      })),
      ...(followUp ? [{ text: followUp.summary, at: formatTime(followUp.occurredAt), sources: [{ kind: "event", ref: String(followUp.streamVersion), href: workspace(incident.summary.incidentId, "#timeline-title") }] }] : []),
    ],
    anchor: "#execution-title",
  };
}

function investigation({ incident, executions }: Records): Stage | undefined {
  const steps = investigationProgress(incident);
  if (steps.length === 0) {
    return undefined;
  }

  const byInvestigation = new Map((executions ?? []).filter((execution) => !execution.shadow).map((execution) => [execution.investigationId, execution]));
  const latest = new Map<string, NonNullable<IncidentDetail["investigations"]>[number]>();
  for (const requested of incident.investigations ?? []) {
    latest.set(requested.role ?? "generalist", requested);
  }

  const evidence = incident.evidence ?? [];
  const accepted = evidence.filter((item) => item.status === "accepted").length;
  const reported = steps.filter((step) => step.state === "reported").length;
  const agents = workspace(incident.summary.incidentId, "#agents-title");
  let status: StageStatus = "complete";
  if (steps.some((step) => step.state === "waiting")) {
    status = "active";
  } else if (steps.every((step) => step.state === "failed" || step.state === "timed-out")) {
    status = "failed";
  }

  const toolCalls = [...byInvestigation.values()].reduce((sum, execution) => sum + (execution.toolCalls?.length ?? 0), 0);
  return {
    id: "investigation",
    title: "Investigation",
    question: "What did each specialist examine?",
    status,
    summary:
      `${reported} of ${plural(steps.length, "investigator")} reported. ` +
      (executions
        ? `They made ${plural(toolCalls, "typed tool call")}; Runtime accepted ${plural(accepted, "evidence item")} citing what those calls returned and rejected ${evidence.length - accepted}.`
        : `Runtime accepted ${plural(accepted, "evidence item")} and rejected ${evidence.length - accepted}. Agent-execution detail is unavailable, so the models' tool calls are not shown.`),
    at: steps.map((step) => step.completedAt).filter((value): value is string => Boolean(value)).sort().at(-1),
    facts: steps.map((step) => {
      const requested = latest.get(step.role);
      const execution = requested ? byInvestigation.get(requested.investigationId) : undefined;
      const model = execution
        ? `; ${execution.modelProvider}/${execution.model}, prompt ${execution.promptVersion}, ${plural(execution.toolCalls?.length ?? 0, "tool call")}, ${execution.inputTokens + execution.outputTokens} tokens`
        : "";
      const query = requested?.query ? `; asked: ${requested.query}` : "";
      return {
        text: `${step.role}: ${step.state}, ${plural(step.evidence, "accepted evidence item")}, ${plural(step.hypotheses, "hypothesis", "hypotheses")}${model}${query}`,
        at: step.completedAt ? formatTime(step.completedAt) : undefined,
        sources: execution ? [{ kind: "execution", ref: execution.agentExecutionId, href: agents }] : [{ kind: "investigation", ref: requested?.investigationId ?? step.role, href: agents }],
      };
    }),
    anchor: "#agents-title",
  };
}

// Phase E03's S9: instructions to AI agents found in untrusted tool results, recorded to be shown and never acted on.
function manipulation({ incident }: Records): Stage | undefined {
  const found = incident.suspectedInjections ?? [];
  if (found.length === 0) {
    return undefined;
  }

  return {
    id: "manipulation",
    title: "Manipulation",
    question: "Was Sentinel told what to conclude?",
    status: "complete",
    summary: `Untrusted ${[...new Set(found.map((item) => item.tool))].join(" and ")} results held ${plural(found.length, "instruction")} addressed to AI agents. Sentinel recorded them to be shown here and did not act on them: they are data, not instructions.`,
    facts: found.map((item) => ({
      text: `${item.serviceId ? `${item.serviceId}: ` : ""}"${item.excerpt}"`,
      sources: [{ kind: "tool", ref: `${item.tool} ${item.source}`, href: workspace(incident.summary.incidentId, "") }],
    })),
    anchor: "",
  };
}

function hypothesis({ incident }: Records): Stage | undefined {
  const compared = comparedHypotheses(incident);
  if (compared.length === 0) {
    return undefined;
  }

  const leading = compared.find((item) => item.standing === "leading");
  const href = workspace(incident.summary.incidentId, "#hypotheses-title");
  return {
    id: "hypothesis",
    title: "Hypotheses",
    question: "What explanations competed?",
    status: "complete",
    summary: `${plural(compared.length, "explanation")} competed. ${leading ? `"${leading.statement}" led.` : "None led."} ${plural(compared.filter((item) => item.standing === "eliminated").length, "was", "were")} eliminated.`,
    facts: compared.map((item) => ({
      text: `${item.standing}: ${item.statement}${item.score !== undefined ? ` (score ${item.score.toFixed(2)}, ${item.support ?? 0} independent support, ${item.contradiction ?? 0} against)` : ""}`,
      sources: [{ kind: "hypothesis", ref: item.id, href }],
    })),
    anchor: "#hypotheses-title",
  };
}

function challenge({ incident }: Records): Stage | undefined {
  const challenges = incident.challenges ?? [];
  if (challenges.length === 0) {
    return undefined;
  }

  const compared = comparedHypotheses(incident);
  const statement = (id: string) => compared.find((item) => item.id === id)?.statement ?? id;
  const falsified = challenges.filter((item) => item.verdict === "falsified");
  const easy = falsified[0];
  return {
    id: "challenge",
    title: "Challenge",
    question: "What easy answer failed scrutiny?",
    status: "complete",
    summary:
      `The verifier ran ${plural(challenges.length, "falsifier")} against ${plural(new Set(challenges.map((item) => item.hypothesisId)).size, "explanation")}; ${falsified.length} falsified one.` +
      (easy ? ` "${statement(easy.hypothesisId)}" failed: ${easy.detail}` : " Every explanation challenged survived."),
    facts: challenges.map((item) => ({
      text: `${item.falsifier} ${item.verdict} "${statement(item.hypothesisId)}": ${item.detail}`,
      sources: item.evidenceIds.length
        ? item.evidenceIds.map((id) => ({ kind: "evidence", ref: id, href: workspace(incident.summary.incidentId, `#evidence-${id}`) }))
        : [{ kind: "challenge", ref: item.investigationId, href: workspace(incident.summary.incidentId, "#hypotheses-title") }],
    })),
    anchor: "#hypotheses-title",
  };
}

function decision({ incident }: Records): Stage | undefined {
  const made = incident.decision;
  if (!made) {
    return undefined;
  }

  const leading = leadingHypothesis(incident);
  const href = workspace(incident.summary.incidentId, "#decision-title");
  const undecided = contenders(incident);
  let summary = `Sentinel diagnosed "${leading?.finding.statement ?? made.leadingHypothesisId}" at ${percent(made.confidence)} confidence, on evidence ${percent(made.evidenceCompleteness)} complete, with ${made.algorithmVersion ?? "consensus"}.`;
  if (made.outcome === "abstained") {
    summary =
      made.abstainedFor === "ambiguous" && undecided.length === 2
        ? `Sentinel abstained: "${undecided[0]}" and "${undecided[1]}" stood too close to choose between, so it escalated rather than guess.`
        : `Sentinel abstained rather than guess: ${made.rationale}`;
  }

  return {
    id: "decision",
    title: "Decision",
    question: "Why act or abstain?",
    status: made.outcome === "diagnosed" ? "complete" : "inconclusive",
    summary,
    at: made.decidedAt,
    facts: [
      ...(made.checks ?? []).map((check) => ({ text: `${check.passed ? "Passed" : "Failed"}: ${check.detail}`, sources: [{ kind: "decision", ref: made.decisionId, href }] })),
      ...(made.missingEvidence ?? []).map((missing) => ({ text: `Could not determine: ${missing}`, sources: [{ kind: "decision", ref: made.decisionId, href }] })),
      ...(made.inputsHash ? [{ text: `Decided on inputs ${made.inputsHash}`, at: formatTime(made.decidedAt), sources: [{ kind: "decision", ref: made.decisionId, href }] }] : []),
    ],
    anchor: "#decision-title",
  };
}

function counterfactualStage({ incident, executions }: Records): Stage | undefined {
  const compared = counterfactual(executions, incident);
  if (!compared) {
    return undefined;
  }

  const shadow = (executions ?? []).filter((execution) => execution.shadow).sort((left, right) => right.startedAt.localeCompare(left.startedAt))[0];
  const summaries: Record<typeof compared.verdict, string> = {
    agrees: `The deploy-blame baseline would have rolled back ${compared.target} as well: here the latest deployment is the cause.`,
    rejected: `The deploy-blame baseline would have rolled back ${compared.target}. Sentinel rejected that explanation${compared.reason ? `: ${compared.reason}` : ""}. It proposed ${compared.instead ?? "no action"} instead.`,
    outranked: `The deploy-blame baseline would have rolled back ${compared.target}. Sentinel ranked that explanation below a stronger one${compared.reason ? ` (${compared.reason})` : ""} and proposed ${compared.instead ?? "no action"} instead.`,
    unexamined: `The deploy-blame baseline would have rolled back ${compared.target}, a change no Sentinel investigator proposed as the cause. Sentinel proposed ${compared.instead ?? "no action"}.`,
    abstained: "The deploy-blame baseline found no deployment to blame and would have done nothing.",
  };
  return {
    id: "counterfactual",
    title: "Counterfactual",
    question: "What would deploy-blame have done?",
    status: "complete",
    summary: summaries[compared.verdict],
    at: shadow?.completedAt,
    facts: shadow ? [{ text: `The baseline ran in shadow beside the investigation, recorded and never sent (${shadow.promptVersion})`, sources: [{ kind: "execution", ref: shadow.agentExecutionId, href: workspace(incident.summary.incidentId, "#agents-title") }] }] : [],
    anchor: "",
  };
}

function governance({ incident }: Records): Stage | undefined {
  const view = incident.plan;
  if (!view) {
    return undefined;
  }

  const { plan, policyEvaluation } = view;
  const href = workspace(incident.summary.incidentId, "#plan-title");
  const actions = plan.actions.map(describeAction).join("; ");
  const statuses: Record<typeof view.status, StageStatus> = {
    awaitingApproval: "active",
    suggestionOnly: "inconclusive",
    denied: "failed",
    approved: "complete",
    rejected: "failed",
    executing: "complete",
    executed: "complete",
    failed: "complete",
    superseded: "complete",
  };
  const approval: Record<typeof view.status, string> = {
    awaitingApproval: "It waits for a person to approve it.",
    suggestionOnly: "Policy permits it only as a suggestion, so nothing runs and responders decide.",
    denied: "Policy denied it, so nothing runs.",
    approved: `${view.approvedBy ?? "An approver"} approved it.`,
    rejected: "An approver rejected it, so nothing runs.",
    executing: `${view.approvedBy ?? "An approver"} approved it.`,
    executed: `${view.approvedBy ?? "An approver"} approved it.`,
    failed: `${view.approvedBy ?? "An approver"} approved it.`,
    superseded: "A later plan replaced it.",
  };
  return {
    id: "governance",
    title: "Governance",
    question: "Who or what allowed the plan?",
    status: statuses[view.status],
    summary: `Plan v${plan.planVersion}: ${actions}. Policy ${policyEvaluation.policyVersion}: ${policyVerdict(policyEvaluation.decision)}. ${approval[view.status]}`,
    at: view.approvedAt ?? policyEvaluation.evaluatedAt,
    facts: [
      ...policyEvaluation.ruleTrace.map((rule) => ({ text: rule, at: formatTime(policyEvaluation.evaluatedAt), sources: [{ kind: "policy", ref: policyEvaluation.policyVersion, href }] })),
      { text: `Plan hash ${plan.planHash}`, sources: [{ kind: "plan", ref: plan.planId, href }] },
      ...(view.approvedBy ? [{ text: `Approved by ${view.approvedBy}`, at: view.approvedAt ? formatTime(view.approvedAt) : undefined, sources: [{ kind: "plan", ref: plan.planId, href }] }] : []),
    ],
    anchor: "#plan-title",
  };
}

function action({ incident }: Records): Stage | undefined {
  const execution = incident.execution;
  if (!execution) {
    return undefined;
  }

  const href = workspace(incident.summary.incidentId, "#execution-title");
  let status: StageStatus = "active";
  if (execution.outcome === "actionVerified" || execution.outcome === "compensated") {
    status = "complete";
  } else if (execution.outcome === "outcomeUnknown") {
    status = "inconclusive";
  } else if (execution.outcome) {
    status = "failed";
  }

  const actions = (incident.plan?.plan.actions ?? []).map(describeAction).join("; ");
  return {
    id: "action",
    title: "Action",
    question: "What changed in the environment?",
    status,
    summary: execution.outcome
      ? `${executionLabel(execution.outcome)}: ${actions}.${execution.detail ? ` ${execution.detail}` : ""}`
      : `Executor is carrying out ${actions}.`,
    at: execution.completedAt ?? execution.startedAt ?? execution.requestedAt,
    facts: [
      { text: "Execution requested with the signed approval", at: formatTime(execution.requestedAt), sources: [{ kind: "plan", ref: execution.planId, href }] },
      ...(execution.startedAt ? [{ text: "Start confirmed, the point of no return", at: formatTime(execution.startedAt), sources: [{ kind: "plan", ref: execution.planId, href }] }] : []),
      ...(execution.completedAt ? [{ text: executionLabel(execution.outcome), at: formatTime(execution.completedAt), sources: [{ kind: "plan", ref: execution.planId, href }] }] : []),
    ],
    anchor: "#execution-title",
  };
}

function recovery({ incident }: Records): Stage | undefined {
  const judged = incident.recovery;
  const href = workspace(incident.summary.incidentId, "#execution-title");
  if (!judged) {
    return incident.summary.status === "verifying"
      ? { id: "recovery", title: "Recovery", question: "Did the system improve?", status: "active", summary: "Sentinel is measuring the recovery signals over the plan's observation window.", facts: [], anchor: "#execution-title" }
      : undefined;
  }

  const met = judged.measurements.filter((measurement) => measurement.met).length;
  let status: StageStatus = "failed";
  if (judged.outcome === "recovered") {
    status = "complete";
  } else if (judged.outcome === "inconclusive") {
    status = "inconclusive";
  }

  return {
    id: "recovery",
    title: "Recovery",
    question: "Did the system improve?",
    status,
    summary:
      judged.outcome === "recovered"
        ? `Independent telemetry proved recovery: ${met} of ${plural(judged.measurements.length, "criterion", "criteria")} met through ${formatTime(judged.observedThrough)}. The adapter's success alone never resolves an incident.`
        : `Recovery ${judged.outcome}: ${met} of ${plural(judged.measurements.length, "criterion", "criteria")} met${judged.reason ? `. ${judged.reason}` : "."}`,
    at: judged.evaluatedAt,
    facts: judged.measurements.map((measurement) => ({
      text:
        `${describeCriterion(measurement.criterion)}: ${measurement.observedValue === undefined ? "no data" : `observed ${measurement.observedValue}`}` +
        `${measurement.baselineValue === undefined ? "" : `, ${measurement.baselineValue} before the action`}, ${measurement.met ? "met" : "not met"}`,
      at: formatTime(judged.observedThrough),
      sources: [{ kind: "recovery", ref: judged.planId, href }],
    })),
    anchor: "#execution-title",
  };
}

function outcome({ incident }: Records): StoryOutcome {
  const { status } = incident.summary;
  const plan = incident.plan;
  const next: Record<IncidentStatus, string> = {
    detected: "Wait for the investigators to be assigned.",
    investigating: "Wait for the investigators to report.",
    awaitingApproval: plan ? `An approver reviews plan v${plan.plan.planVersion} in the workspace.` : "An approver reviews the plan in the workspace.",
    remediating: "Wait for the action's result.",
    verifying: "Wait for the recovery window to close.",
    resolved: "Nothing: telemetry proved recovery.",
    escalated: "Responders take over, starting from what the decision could not determine.",
  };
  return {
    incident: status,
    action: incident.execution?.outcome ? executionLabel(incident.execution.outcome) : (plan ? `Plan v${plan.plan.planVersion} ${plan.status}` : "No action proposed"),
    recovery: incident.recovery?.outcome,
    next: next[status],
  };
}

/** The story of one incident, from its records. Pure: the same records always give the same story. */
export function deriveStory(incident: IncidentDetail, executions: AgentExecution[] | null): Story {
  const records: Records = { incident, executions };
  const stages = [context, detection, correction, investigation, manipulation, hypothesis, challenge, decision, counterfactualStage, governance, action, recovery]
    .map((build) => build(records))
    .filter((stage): stage is Stage => stage !== undefined);
  return {
    incidentId: incident.summary.incidentId,
    stages,
    outcome: outcome(records),
    executionsAvailable: executions !== null,
    providers: [...new Set((executions ?? []).filter((execution) => !execution.shadow).map((execution) => execution.modelProvider))].sort(),
  };
}

/** Why a run record cannot be played, or undefined when it can. */
export function unplayable(record: IncidentRunRecord): string | undefined {
  if (!playableRunRecords.includes(record.schemaVersion)) {
    return `This run record has schema version ${record.schemaVersion}; Judge mode plays versions ${playableRunRecords.join(" and ")}.`;
  }
  return record.incident?.summary ? undefined : "This run record holds no incident.";
}
