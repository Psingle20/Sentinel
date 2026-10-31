import { CircleCheck, CircleX, Clock, Hourglass, RefreshCcw, Repeat, ShieldAlert, ShieldCheck, type LucideIcon } from "lucide-react";
import Link from "next/link";
import type { ComponentProps } from "react";

import { ConfidenceDisplay } from "@/design-system/domain/confidence-display";
import { IncidentStatusBadge } from "@/design-system/domain/incident-status-badge";
import { SeverityIndicator } from "@/design-system/domain/severity-indicator";
import { StatusBadge } from "@/design-system/domain/status-badge";
import { AgentActivity } from "@/design-system/patterns/agent-activity";
import { DataState } from "@/design-system/patterns/data-state";
import { EvidenceGraph } from "@/design-system/patterns/evidence-graph";
import { HypothesisComparison } from "@/design-system/patterns/hypothesis-comparison";
import { RecoveryPanel } from "@/design-system/patterns/recovery-panel";
import { RemediationReview } from "@/design-system/patterns/remediation-review";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import type { AgentExecution, IncidentDetail, RecoveryOutcome, SimilarIncident } from "@/lib/api/contracts";

import { EvidenceTable } from "./evidence-table";
import { PlanDecision, RevokeApproval } from "./plan-decision";
import { TimelineList } from "./timeline-list";
import { describeAction, describeCriterion, executionLabel, formatDateTime, formatTime, incidentStatus, percent, recoveryStatus } from "./presentation";
import {
  agentRuns,
  comparedHypotheses,
  contenders,
  counterfactual,
  escalationOf,
  graphOf,
  investigationProgress,
  leadingHypothesis,
  reviewedPlan,
  seenBefore,
  type Counterfactual,
  type CounterfactualVerdict,
  type InvestigationStepState,
} from "./view-models";

export function IncidentHeaderBadges({ incident }: { incident: IncidentDetail }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <SeverityIndicator severity={incident.summary.severity} />
      <IncidentStatusBadge status={incidentStatus(incident.summary.status)} />
      <span className="text-body text-text-muted">
        {incident.summary.affectedServiceId} · opened {formatDateTime(incident.summary.openedAt)}
        {incident.cycle ? ` · cycle ${incident.cycle + 1}` : ""}
      </span>
      <Link
        href={`/incidents/${encodeURIComponent(incident.summary.incidentId)}/postmortem`}
        className="text-body text-text-muted underline-offset-4 hover:text-text-primary hover:underline focus-visible:text-text-primary"
      >
        Postmortem and replay
      </Link>
    </div>
  );
}

export function IncidentDetailView({
  incident,
  executions,
  similar,
}: {
  incident: IncidentDetail;
  executions: AgentExecution[] | null;
  similar: SimilarIncident[] | null;
}) {
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="flex flex-col gap-6 lg:col-span-2">
        <SeenBeforeSection incident={incident} similar={similar} />
        <InvestigationProgressSection incident={incident} />
        <SelfCorrectionSection incident={incident} />
        <InjectionSection incident={incident} />
        <DecisionSection incident={incident} />
        <DamageAvoidedSection incident={incident} executions={executions} />
        <PlanSection incident={incident} />
        <ExecutionSection incident={incident} />
        <HypothesesSection incident={incident} />
        <GraphSection incident={incident} />
        <EvidenceSection incident={incident} />
      </div>
      <div className="flex flex-col gap-6">
        <ImpactSection incident={incident} />
        <TimelineSection incident={incident} />
        <AgentActivitySection incident={incident} executions={executions} />
        <SimilarSection similar={similar} />
      </div>
    </div>
  );
}

type BadgeTone = ComponentProps<typeof StatusBadge>["tone"];

// Phase E03: a repeat of a remembered incident leads the page, citing the earlier one and what resolved it. It is a hint
// for responders and investigators, never evidence.
function SeenBeforeSection({ incident, similar }: { incident: IncidentDetail; similar: SimilarIncident[] | null }) {
  const match = seenBefore(similar, incident.summary.incidentId);
  if (!match) {
    return null;
  }

  return (
    <Card role="region" aria-label="Seen before">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          <StatusBadge tone="leading" icon={Repeat}>
            Seen before
          </StatusBadge>
        </CardTitle>
        <CardDescription>
          Sentinel resolved an incident like this one on {formatDateTime(match.recordedAt)}, {percent(match.similarity)} alike. It is cited
          as a hint; nothing Sentinel concludes rests on it.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-1 text-body">
        <Link
          href={`/incidents/${encodeURIComponent(match.incidentId)}`}
          className="text-text-primary underline-offset-4 hover:underline focus-visible:underline"
        >
          {match.causeStatement ?? "No cause was established"}
        </Link>
        {match.actionSummary ? <span className="text-text-muted">Resolved by: {match.actionSummary}</span> : null}
        <span className="text-caption text-text-muted">Shares {match.sharedFeatures.join(", ")}</span>
      </CardContent>
    </Card>
  );
}

const stepStates: Record<InvestigationStepState, { tone: BadgeTone; icon: LucideIcon; label: string }> = {
  waiting: { tone: "executing", icon: Hourglass, label: "Investigating" },
  reported: { tone: "stable", icon: CircleCheck, label: "Reported" },
  abstained: { tone: "neutral", icon: CircleX, label: "Abstained" },
  failed: { tone: "failed", icon: CircleX, label: "Failed" },
  "timed-out": { tone: "warning", icon: Clock, label: "Timed out" },
};

// Phase E03: the investigation as it happens. The page refetches on every live event, so each specialist's report and
// the verifier's challenge appear as they arrive, until the decision replaces this view.
function InvestigationProgressSection({ incident }: { incident: IncidentDetail }) {
  if (incident.summary.status !== "detected" && incident.summary.status !== "investigating") {
    return null;
  }

  const steps = investigationProgress(incident);
  const reported = steps.filter((step) => step.state !== "waiting").length;
  return (
    <Card role="region" aria-label="Investigation in progress">
      <CardHeader>
        <CardTitle>Investigation in progress</CardTitle>
        <CardDescription>
          {steps.length === 0
            ? "Sentinel is assigning its specialists."
            : `${reported} of ${steps.length} investigators have reported. Sentinel decides once the verifier has challenged what they found.`}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ul aria-label="Investigators" className="grid gap-2 sm:grid-cols-2">
          {steps.map((step) => {
            const presentation = stepStates[step.state];
            return (
              <li key={step.role} className="flex flex-wrap items-center justify-between gap-2 text-body">
                <span className="font-medium text-text-primary">{step.role}</span>
                <span className="flex flex-wrap items-center gap-2">
                  {step.state === "reported" ? (
                    <span className="text-caption text-text-muted">
                      {step.evidence} evidence, {step.hypotheses} {step.hypotheses === 1 ? "hypothesis" : "hypotheses"}
                    </span>
                  ) : null}
                  <StatusBadge tone={presentation.tone} icon={presentation.icon}>
                    {presentation.label}
                  </StatusBadge>
                </span>
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
  );
}

const recoveryWords: Record<RecoveryOutcome, string> = {
  recovered: "recovered",
  unchanged: "unchanged",
  degraded: "degraded",
  inconclusive: "inconclusive",
  compensationRequired: "worse than before",
};

// Phase E03: a remedy that took effect at its target without ending the incident, and the follow-up cycle it opened.
function SelfCorrectionSection({ incident }: { incident: IncidentDetail }) {
  const tried = (incident.remediesTried ?? []).filter((remedy) => remedy.followedUp);
  if (tried.length === 0) {
    return null;
  }

  return (
    <Card role="region" aria-label="Self-correction">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          <StatusBadge tone="warning" icon={RefreshCcw}>
            First remedy did not end the incident
          </StatusBadge>
        </CardTitle>
        <CardDescription>
          The remedy took effect at its target, yet the expected signals did not recover, so something else still held the symptom.
          Sentinel investigated again, knowing what it had tried, and it never repeats a remedy.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ul className="flex flex-col gap-3 text-body" aria-label="Remedies tried">
          {tried.map((remedy) => (
            <li key={remedy.planId} className="flex flex-col gap-1">
              <span className="text-text-primary">
                Cycle {remedy.cycle + 1}: {remedy.summary}
              </span>
              <span className="text-text-muted">
                Recovery {recoveryWords[remedy.outcome]} at {formatTime(remedy.judgedAt)}
                {remedy.reason ? `: ${remedy.reason}` : ""}
              </span>
              {remedy.measurements.length > 0 ? (
                <ul className="flex flex-col gap-0.5 text-caption text-text-muted" aria-label={`Recovery measurements of cycle ${remedy.cycle + 1}`}>
                  {remedy.measurements.map((measurement) => (
                    <li key={`${measurement.criterion.serviceId}-${measurement.criterion.metricName}`}>
                      {describeCriterion(measurement.criterion)}: {measurement.observedValue === undefined ? "no reading" : measurement.observedValue}
                      {measurement.met ? ", met" : ", not met"}
                    </li>
                  ))}
                </ul>
              ) : null}
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

const counterfactualVerdicts: Record<CounterfactualVerdict, { tone: BadgeTone; icon: LucideIcon; label: string }> = {
  agrees: { tone: "neutral", icon: CircleCheck, label: "Baseline agrees" },
  rejected: { tone: "stable", icon: ShieldCheck, label: "Damage avoided" },
  outranked: { tone: "stable", icon: ShieldCheck, label: "Damage avoided" },
  unexamined: { tone: "stable", icon: ShieldCheck, label: "Damage avoided" },
  abstained: { tone: "neutral", icon: CircleX, label: "Baseline would do nothing" },
};

function counterfactualSentences(view: Counterfactual): string[] {
  switch (view.verdict) {
    case "agrees":
      return [`The deploy-blame baseline would have rolled back ${view.target} as well: here the latest deployment is the cause.`];
    case "abstained":
      return ["The deploy-blame baseline found no deployment to blame and would have done nothing."];
    case "rejected":
      return [`The deploy-blame baseline would have rolled back ${view.target}.`, `Sentinel eliminated that explanation${view.reason ? `: ${view.reason}` : "."}`];
    case "outranked":
      return [`The deploy-blame baseline would have rolled back ${view.target}.`, `Sentinel ranked that explanation below the one it leads with${view.reason ? `: ${view.reason}` : "."}`];
    default:
      return [`The deploy-blame baseline would have rolled back ${view.target}.`, "No investigator found evidence that the change explains the symptoms."];
  }
}

// Phase E03: beside the decision, what the deploy-blame baseline would have done and Sentinel's own verdict on it.
function DamageAvoidedSection({ incident, executions }: { incident: IncidentDetail; executions: AgentExecution[] | null }) {
  const view = counterfactual(executions, incident);
  if (!view) {
    return null;
  }

  const presentation = counterfactualVerdicts[view.verdict];
  return (
    <Card role="region" aria-label="Damage avoided">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          <StatusBadge tone={presentation.tone} icon={presentation.icon}>
            {presentation.label}
          </StatusBadge>
        </CardTitle>
        <CardDescription>
          The deploy-blame baseline runs beside every investigation and blames the latest deployment. It is recorded, never acted on.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-1 text-body">
        {counterfactualSentences(view).map((sentence) => (
          <p key={sentence} className="text-text-primary">
            {sentence}
          </p>
        ))}
        {view.instead && view.verdict !== "agrees" ? <p className="text-text-muted">Sentinel proposes instead: {view.instead}</p> : null}
      </CardContent>
    </Card>
  );
}

const injectionSources: Record<string, string> = {
  log_signatures: "logs",
  search_runbooks: "runbooks",
};

// Instructions to AI agents that the investigations found in logs or runbooks. They are data: shown so that responders
// know someone tried, never a reason for anything Sentinel concluded or proposed.
function InjectionSection({ incident }: { incident: IncidentDetail }) {
  const attempts = incident.suspectedInjections ?? [];
  if (attempts.length === 0) {
    return null;
  }

  const bySource = [...new Map(attempts.map((attempt) => [attempt.source, attempt])).values()];
  return (
    <Card role="region" aria-label="Instructions to AI agents">
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center gap-2">
          <StatusBadge tone="warning" icon={ShieldAlert}>
            Manipulation attempt ignored
          </StatusBadge>
        </CardTitle>
        <CardDescription>
          {bySource.length === 1 ? "An instruction" : `${bySource.length} instructions`} addressed to AI agents turned up in untrusted
          data. Sentinel treated {bySource.length === 1 ? "it" : "them"} as data: nothing it concluded or proposed rests on{" "}
          {bySource.length === 1 ? "it" : "them"}, and every action still needs policy and approval.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ul className="flex flex-col gap-3 text-body">
          {bySource.map((attempt) => (
            <li key={attempt.source} className="flex flex-col gap-1">
              <span className="text-text-muted">
                In {attempt.serviceId ?? "an unknown service"}&apos;s {injectionSources[attempt.tool] ?? attempt.tool}, matching{" "}
                {attempts
                  .filter((match) => match.source === attempt.source)
                  .map((match) => match.pattern)
                  .join(", ")}
              </span>
              <blockquote className="font-mono text-code break-words text-text-primary">{attempt.excerpt}</blockquote>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}

function DecisionSection({ incident }: { incident: IncidentDetail }) {
  const decision = incident.decision;
  const leading = leadingHypothesis(incident);
  const escalation = escalationOf(incident);
  const undecided = contenders(incident);
  return (
    <Card aria-labelledby="decision-title">
      <CardHeader>
        <CardTitle id="decision-title">Decision</CardTitle>
        <CardDescription>What Sentinel concluded, and the checks the leading explanation had to survive.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {decision ? (
          <>
            <div className="flex flex-wrap items-start justify-between gap-4">
              <StatusBadge
                tone={decision.outcome === "diagnosed" ? "leading" : "neutral"}
                icon={decision.outcome === "diagnosed" ? CircleCheck : CircleX}
              >
                {decision.outcome === "diagnosed" ? "Diagnosed" : "Abstained"}
              </StatusBadge>
              <ConfidenceDisplay
                confidence={decision.confidence}
                evidenceCompleteness={decision.evidenceCompleteness}
                calibration={decision.calibration}
              />
            </div>
            {leading ? <p className="text-label text-text-primary">{leading.finding.statement}</p> : null}
            <p className="text-body text-text-primary">{decision.rationale}</p>
            {decision.abstainedFor === "ambiguous" && undecided.length === 2 ? (
              <p className="text-body text-text-primary">
                Sentinel could not choose between “{undecided[0]}” and “{undecided[1]}”, so it escalated rather than guess.
              </p>
            ) : null}
            {escalation?.query ? (
              <p className="text-body text-text-muted" aria-label="Escalation">
                Two explanations stood too close, so before deciding Sentinel asked for the one observation that separates them: {escalation.query}
              </p>
            ) : null}
            <ul className="flex flex-col gap-2" aria-label="Decision checks">
              {(decision.checks ?? []).map((check) => (
                <li key={check.name} className="flex items-start gap-2 text-body">
                  {check.passed ? (
                    <CircleCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-incident-stable" />
                  ) : (
                    <CircleX aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-incident-critical" />
                  )}
                  <span className="text-text-primary">
                    <span className="sr-only">{check.passed ? "Passed: " : "Failed: "}</span>
                    {check.detail}
                  </span>
                </li>
              ))}
            </ul>
            {(decision.missingEvidence ?? []).length > 0 ? (
              <div className="flex flex-col gap-1 text-body">
                <span className="font-medium text-text-primary">What the investigation could not determine</span>
                <ul className="flex flex-col gap-0.5 text-text-muted" aria-label="Missing evidence">
                  {(decision.missingEvidence ?? []).map((missing) => (
                    <li key={missing}>{missing}</li>
                  ))}
                </ul>
              </div>
            ) : null}
            <p className="text-caption text-text-muted">
              {decision.algorithmVersion ?? "falsification-v1"} · decided {formatTime(decision.decidedAt)}
              {decision.inputsHash ? ` · inputs ${decision.inputsHash.slice(0, 16)}` : ""}
            </p>
          </>
        ) : (
          <DataState kind="loading" title="Investigating" description="Sentinel decides once the investigations report." />
        )}
      </CardContent>
    </Card>
  );
}

function PlanSection({ incident }: { incident: IncidentDetail }) {
  const view = incident.plan;
  if (!view) {
    return null;
  }

  const { plan } = view;
  const identity = {
    incidentId: incident.summary.incidentId,
    planId: plan.planId,
    planVersion: plan.planVersion,
    planHash: plan.planHash,
    summary: plan.actions.map(describeAction).join("; "),
  };
  const awaiting = view.status === "awaitingApproval" && incident.summary.status === "awaitingApproval";
  const revocable = view.status === "approved" && !incident.execution?.startedAt;
  return (
    <Card aria-labelledby="plan-title">
      <CardHeader>
        <CardTitle id="plan-title">Remediation plan v{plan.planVersion}</CardTitle>
        <CardDescription>{identity.summary}</CardDescription>
      </CardHeader>
      <CardContent>
        <RemediationReview plan={reviewedPlan(view)}>
          {awaiting ? <PlanDecision plan={identity} /> : null}
          {revocable ? <RevokeApproval plan={identity} /> : null}
          {view.approvedBy ? (
            <p className="text-body text-text-muted">
              Approved by {view.approvedBy} at {view.approvedAt ? formatTime(view.approvedAt) : "an unknown time"}
            </p>
          ) : null}
        </RemediationReview>
      </CardContent>
    </Card>
  );
}

function ExecutionSection({ incident }: { incident: IncidentDetail }) {
  if (!incident.execution && !incident.recovery) {
    return null;
  }

  return (
    <Card aria-labelledby="execution-title">
      <CardHeader>
        <CardTitle id="execution-title">Execution and recovery</CardTitle>
        <CardDescription>Recovery is judged from the expected signals, not from the action&apos;s own response.</CardDescription>
      </CardHeader>
      <CardContent>
        <RecoveryPanel
          execution={incident.execution ? { label: executionLabel(incident.execution.outcome), detail: incident.execution.detail } : undefined}
          recovery={
            incident.recovery
              ? {
                  status: recoveryStatus(incident.recovery.outcome),
                  observedThrough: formatTime(incident.recovery.observedThrough),
                  reason: incident.recovery.reason,
                  measurements: incident.recovery.measurements.map((measurement) => ({
                    criterion: describeCriterion(measurement.criterion),
                    observed: measurement.observedValue === undefined ? undefined : String(measurement.observedValue),
                    met: measurement.met,
                  })),
                }
              : undefined
          }
        />
      </CardContent>
    </Card>
  );
}

function HypothesesSection({ incident }: { incident: IncidentDetail }) {
  const hypotheses = comparedHypotheses(incident);
  return (
    <Card aria-labelledby="hypotheses-title">
      <CardHeader>
        <CardTitle id="hypotheses-title">Hypotheses</CardTitle>
        <CardDescription>Competing explanations side by side, including the ones Sentinel eliminated, and how each fared under challenge.</CardDescription>
      </CardHeader>
      <CardContent>
        {hypotheses.length === 0 ? <DataState kind="empty" title="No hypotheses yet" /> : <HypothesisComparison hypotheses={hypotheses} />}
      </CardContent>
    </Card>
  );
}

function GraphSection({ incident }: { incident: IncidentDetail }) {
  const graph = graphOf(incident);
  return (
    <Card aria-labelledby="graph-title">
      <CardHeader>
        <CardTitle id="graph-title">Evidence graph</CardTitle>
        <CardDescription>How changes, services, hypotheses and evidence connect to the symptom.</CardDescription>
      </CardHeader>
      <CardContent>
        {graph.nodes.length === 0 ? (
          <DataState kind="empty" title="No relations yet" />
        ) : (
          <EvidenceGraph nodes={graph.nodes} edges={graph.edges} leadingIds={graph.leadingIds} title={`Causal graph of ${incident.summary.title}`} />
        )}
      </CardContent>
    </Card>
  );
}

function EvidenceSection({ incident }: { incident: IncidentDetail }) {
  const evidence = incident.evidence ?? [];
  return (
    <Card aria-labelledby="evidence-title">
      <CardHeader>
        <CardTitle id="evidence-title">Evidence</CardTitle>
        <CardDescription>Every claim cites the observations it rests on.</CardDescription>
      </CardHeader>
      <CardContent>
        {evidence.length === 0 ? <DataState kind="empty" title="No evidence yet" /> : <EvidenceTable evidence={evidence} />}
      </CardContent>
    </Card>
  );
}

function ImpactSection({ incident }: { incident: IncidentDetail }) {
  const services = incident.affectedServices ?? [incident.summary.affectedServiceId];
  return (
    <Card aria-labelledby="impact-title">
      <CardHeader>
        <CardTitle id="impact-title">Impact</CardTitle>
        <CardDescription>
          Symptoms from {incident.summary.symptomOnset ? formatTime(incident.summary.symptomOnset) : "an unknown time"}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 text-body">
        <div className="flex flex-col gap-1">
          <span className="font-medium text-text-primary">Affected services</span>
          <span className="text-text-muted">{services.join(", ")}</span>
        </div>
        {(incident.linkedSignals ?? []).length > 0 ? (
          <ul className="flex flex-col gap-1" aria-label="Linked signals">
            {(incident.linkedSignals ?? []).map((signal) => (
              <li key={`${signal.serviceId}-${signal.metricName}`} className="text-text-muted">
                {signal.serviceId} {signal.metricName} at {signal.value.toFixed(2)}, linked with score {signal.score.toFixed(2)}
              </li>
            ))}
          </ul>
        ) : null}
        <div className="flex flex-col gap-1">
          <span className="font-medium text-text-primary">Correlated changes</span>
          {(incident.changes ?? []).length === 0 ? (
            <span className="text-text-muted">None</span>
          ) : (
            <ul className="flex flex-col gap-1" aria-label="Correlated changes">
              {(incident.changes ?? []).map((change) => (
                <li key={change.deploymentId} className="text-text-muted">
                  {change.serviceId} {change.changeKind} {change.version}
                  {change.servingFrom ? `, serving from ${formatTime(change.servingFrom)}` : ""}
                  {change.score === undefined ? "" : `, score ${change.score.toFixed(2)}`}
                </li>
              ))}
            </ul>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

const timelinePage = 50;

function TimelineSection({ incident }: { incident: IncidentDetail }) {
  // Only the latest page goes to the browser; the rest is paged in from Api on request.
  const timeline = [...(incident.timeline ?? [])].sort((left, right) => left.streamVersion - right.streamVersion);
  const latest = timeline.slice(-timelinePage);
  const next = timeline.length > latest.length ? String(latest[0].streamVersion) : undefined;
  return (
    <Card aria-labelledby="timeline-title">
      <CardHeader>
        <CardTitle id="timeline-title">Timeline</CardTitle>
        <CardDescription>The incident stream, in order.</CardDescription>
      </CardHeader>
      <CardContent>
        <TimelineList incidentId={incident.summary.incidentId} latest={latest} total={timeline.length} next={next} />
      </CardContent>
    </Card>
  );
}

function AgentActivitySection({ incident, executions }: { incident: IncidentDetail; executions: AgentExecution[] | null }) {
  return (
    <Card aria-labelledby="agents-title">
      <CardHeader>
        <CardTitle id="agents-title">Agent activity</CardTitle>
        <CardDescription>Which investigator ran, with which model and prompt, and what it cost.</CardDescription>
      </CardHeader>
      <CardContent>
        {executions === null ? (
          <DataState kind="degraded" title="Agent activity is unavailable" description="AgentWorker did not answer; the incident itself is unaffected." />
        ) : executions.length === 0 ? (
          <DataState kind="empty" title="No investigator has run yet" />
        ) : (
          <AgentActivity runs={agentRuns(executions, incident)} />
        )}
      </CardContent>
    </Card>
  );
}

function SimilarSection({ similar }: { similar: SimilarIncident[] | null }) {
  return (
    <Card aria-labelledby="similar-title">
      <CardHeader>
        <CardTitle id="similar-title">Similar incidents</CardTitle>
        <CardDescription>Remembered incidents of this tenant that look alike. Hints, never evidence.</CardDescription>
      </CardHeader>
      <CardContent>
        {similar === null ? (
          <DataState kind="degraded" title="Incident memory is unavailable" />
        ) : similar.length === 0 ? (
          <DataState kind="empty" title="Nothing similar is remembered" />
        ) : (
          <ul className="flex flex-col gap-3 text-body" aria-label="Similar incidents">
            {similar.map((match) => (
              <li key={match.incidentId} className="flex flex-col gap-0.5">
                <Link
                  href={`/incidents/${encodeURIComponent(match.incidentId)}`}
                  className="text-text-primary underline-offset-4 hover:underline focus-visible:underline"
                >
                  {match.causeStatement ?? "No cause was established"}
                </Link>
                <span className="text-caption text-text-muted">
                  {percent(match.similarity)} similar · {match.outcome} · shares {match.sharedFeatures.join(", ")}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
