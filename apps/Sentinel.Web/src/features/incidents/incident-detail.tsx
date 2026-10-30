import { CircleCheck, CircleX } from "lucide-react";
import Link from "next/link";

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
import type { AgentExecution, IncidentDetail, SimilarIncident } from "@/lib/api/contracts";

import { EvidenceTable } from "./evidence-table";
import { PlanDecision, RevokeApproval } from "./plan-decision";
import { TimelineList } from "./timeline-list";
import { describeAction, describeCriterion, executionLabel, formatDateTime, formatTime, incidentStatus, percent, recoveryStatus } from "./presentation";
import { agentRuns, comparedHypotheses, graphOf, leadingHypothesis, reviewedPlan } from "./view-models";

export function IncidentHeaderBadges({ incident }: { incident: IncidentDetail }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <SeverityIndicator severity={incident.summary.severity} />
      <IncidentStatusBadge status={incidentStatus(incident.summary.status)} />
      <span className="text-sm text-text-muted">
        {incident.summary.affectedServiceId} · opened {formatDateTime(incident.summary.openedAt)}
        {incident.cycle ? ` · cycle ${incident.cycle + 1}` : ""}
      </span>
      <Link
        href={`/incidents/${encodeURIComponent(incident.summary.incidentId)}/postmortem`}
        className="text-sm text-text-muted underline-offset-4 hover:text-text-primary hover:underline focus-visible:text-text-primary"
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
        <DecisionSection incident={incident} />
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

function DecisionSection({ incident }: { incident: IncidentDetail }) {
  const decision = incident.decision;
  const leading = leadingHypothesis(incident);
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
            {leading ? <p className="text-sm font-medium text-text-primary">{leading.finding.statement}</p> : null}
            <p className="text-sm text-text-primary">{decision.rationale}</p>
            <ul className="flex flex-col gap-2" aria-label="Decision checks">
              {(decision.checks ?? []).map((check) => (
                <li key={check.name} className="flex items-start gap-2 text-sm">
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
              <div className="flex flex-col gap-1 text-sm">
                <span className="font-medium text-text-primary">What the investigation could not determine</span>
                <ul className="flex flex-col gap-0.5 text-text-muted" aria-label="Missing evidence">
                  {(decision.missingEvidence ?? []).map((missing) => (
                    <li key={missing}>{missing}</li>
                  ))}
                </ul>
              </div>
            ) : null}
            <p className="text-xs text-text-muted">
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
            <p className="text-sm text-text-muted">
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
      <CardContent className="flex flex-col gap-3 text-sm">
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
          <ul className="flex flex-col gap-3 text-sm" aria-label="Similar incidents">
            {similar.map((match) => (
              <li key={match.incidentId} className="flex flex-col gap-0.5">
                <Link
                  href={`/incidents/${encodeURIComponent(match.incidentId)}`}
                  className="text-text-primary underline-offset-4 hover:underline focus-visible:underline"
                >
                  {match.causeStatement ?? "No cause was established"}
                </Link>
                <span className="text-xs text-text-muted">
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
