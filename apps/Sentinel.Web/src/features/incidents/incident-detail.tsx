import { CircleCheck, CircleX } from "lucide-react";

import { ApprovalState } from "@/design-system/domain/approval-state";
import { ConfidenceDisplay } from "@/design-system/domain/confidence-display";
import { EvidenceDirectionBadge } from "@/design-system/domain/evidence-direction-badge";
import { IncidentStatusBadge } from "@/design-system/domain/incident-status-badge";
import { RecoveryState } from "@/design-system/domain/recovery-state";
import { SeverityIndicator } from "@/design-system/domain/severity-indicator";
import { StatusBadge } from "@/design-system/domain/status-badge";
import { DataState } from "@/design-system/patterns/data-state";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import type { HypothesisView, IncidentDetail } from "@/lib/api/contracts";

import { PlanDecision } from "./plan-decision";
import {
  approvalStatus,
  describeAction,
  describeCriterion,
  executionLabel,
  formatDateTime,
  formatTime,
  incidentStatus,
  percent,
  recoveryStatus,
} from "./presentation";

export function IncidentHeaderBadges({ incident }: { incident: IncidentDetail }) {
  return (
    <div className="flex flex-wrap items-center gap-2">
      <SeverityIndicator severity={incident.summary.severity} />
      <IncidentStatusBadge status={incidentStatus(incident.summary.status)} />
      <span className="text-sm text-text-muted">
        {incident.summary.affectedServiceId} · opened {formatDateTime(incident.summary.openedAt)}
      </span>
    </div>
  );
}

export function IncidentDetailView({ incident }: { incident: IncidentDetail }) {
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="flex flex-col gap-6 lg:col-span-2">
        <DecisionSection incident={incident} />
        <PlanSection incident={incident} />
        <HypothesesSection incident={incident} />
        <EvidenceSection incident={incident} />
      </div>
      <div className="flex flex-col gap-6">
        <TimelineSection incident={incident} />
        <InvestigationsSection incident={incident} />
      </div>
    </div>
  );
}

function DecisionSection({ incident }: { incident: IncidentDetail }) {
  const decision = incident.decision;
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
          </>
        ) : (
          <DataState kind="loading" title="Investigating" description="Sentinel decides once the investigation reports." />
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

  const { plan, policyEvaluation } = view;
  const summary = plan.actions.map(describeAction).join("; ");
  return (
    <Card aria-labelledby="plan-title">
      <CardHeader>
        <CardTitle id="plan-title">Remediation plan v{plan.planVersion}</CardTitle>
        <CardDescription>{summary}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-2">
          <ApprovalState status={approvalStatus(view.status)} />
          <span className="text-sm text-text-muted">
            Risk {plan.risk} · policy {policyEvaluation.policyVersion}: {policyEvaluation.decision}
          </span>
        </div>
        <ul className="flex flex-col gap-1 text-sm text-text-muted" aria-label="Policy rules">
          {policyEvaluation.ruleTrace.map((rule) => (
            <li key={rule}>{rule}</li>
          ))}
        </ul>
        <div className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-text-primary">Expected recovery within {plan.observationWindow}</span>
          {plan.expectedRecovery.map((criterion) => (
            <span key={`${criterion.serviceId}-${criterion.metricName}`} className="text-text-muted">
              {describeCriterion(criterion)}
            </span>
          ))}
        </div>
        <p className="font-mono text-xs break-all text-text-muted">{plan.planHash}</p>

        {view.status === "awaitingApproval" && incident.summary.status === "awaitingApproval" ? (
          <PlanDecision
            plan={{ incidentId: incident.summary.incidentId, planId: plan.planId, planVersion: plan.planVersion, planHash: plan.planHash, summary }}
          />
        ) : null}
        {view.approvedBy ? (
          <p className="text-sm text-text-muted">
            Approved by {view.approvedBy} at {view.approvedAt ? formatTime(view.approvedAt) : "an unknown time"}
          </p>
        ) : null}

        {incident.execution ? (
          <div className="flex flex-col gap-1 text-sm" aria-label="Execution">
            <span className="font-medium text-text-primary">{executionLabel(incident.execution.outcome)}</span>
            {incident.execution.detail ? <span className="text-text-muted">{incident.execution.detail}</span> : null}
          </div>
        ) : null}

        {incident.recovery ? (
          <div className="flex flex-col gap-2" aria-label="Recovery">
            <RecoveryState status={recoveryStatus(incident.recovery.outcome)} />
            {incident.recovery.measurements.map((measurement) => (
              <span key={measurement.criterion.metricName} className="text-sm text-text-muted">
                {describeCriterion(measurement.criterion)}: observed {measurement.observedValue ?? "no data"}
                {measurement.met === undefined ? "" : measurement.met ? " (met)" : " (not met)"}
              </span>
            ))}
            {incident.recovery.reason ? <span className="text-sm text-text-muted">{incident.recovery.reason}</span> : null}
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function HypothesesSection({ incident }: { incident: IncidentDetail }) {
  const hypotheses = incident.hypotheses ?? [];
  return (
    <Card aria-labelledby="hypotheses-title">
      <CardHeader>
        <CardTitle id="hypotheses-title">Hypotheses</CardTitle>
        <CardDescription>Competing explanations, including the ones Sentinel rejected.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {hypotheses.length === 0 ? (
          <DataState kind="empty" title="No hypotheses yet" />
        ) : (
          hypotheses.map((hypothesis) => <HypothesisItem key={hypothesis.finding.hypothesisId} hypothesis={hypothesis} />)
        )}
      </CardContent>
    </Card>
  );
}

function HypothesisItem({ hypothesis }: { hypothesis: HypothesisView }) {
  const leading = hypothesis.status === "supported" || hypothesis.status === "confirmed";
  return (
    <div className="flex flex-col gap-1 rounded-md border border-border-subtle p-3">
      <div className="flex flex-wrap items-center gap-2">
        <StatusBadge tone={leading ? "leading" : hypothesis.status === "rejected" ? "contradicting" : "neutral"} icon={leading ? CircleCheck : CircleX}>
          {hypothesis.status}
        </StatusBadge>
        {hypothesis.finding.suspectedServiceId ? (
          <span className="text-xs text-text-muted">{hypothesis.finding.suspectedServiceId}</span>
        ) : null}
      </div>
      <p className="text-sm text-text-primary">{hypothesis.finding.statement}</p>
      <p className="text-xs text-text-muted">{hypothesis.finding.causalChain.join(" → ")}</p>
    </div>
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
        {evidence.length === 0 ? (
          <DataState kind="empty" title="No evidence yet" />
        ) : (
          <Table aria-label="Evidence">
            <TableHeader>
              <TableRow>
                <TableHead>Direction</TableHead>
                <TableHead>Finding</TableHead>
                <TableHead>Strength</TableHead>
                <TableHead>Sources</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {evidence.map((item) => (
                <TableRow key={item.finding.evidenceId}>
                  <TableCell>
                    <EvidenceDirectionBadge direction={item.finding.direction} />
                  </TableCell>
                  <TableCell className="whitespace-normal">
                    <span className="text-text-primary">{item.finding.statement}</span>
                    {item.status === "rejected" ? (
                      <span className="block text-xs text-incident-critical">Rejected: {item.rejectionReason}</span>
                    ) : null}
                    {item.finding.partial ? <span className="block text-xs text-incident-warning">Based on partial data</span> : null}
                  </TableCell>
                  <TableCell>{percent(item.finding.strength)}</TableCell>
                  <TableCell className="text-text-muted">
                    <details>
                      <summary className="cursor-pointer">{item.finding.sourceObservationIds.length} observation(s)</summary>
                      <ul className="mt-1 flex flex-col gap-0.5 font-mono text-xs break-all">
                        {item.finding.sourceObservationIds.map((id) => (
                          <li key={id}>{id}</li>
                        ))}
                      </ul>
                    </details>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function TimelineSection({ incident }: { incident: IncidentDetail }) {
  return (
    <Card aria-labelledby="timeline-title">
      <CardHeader>
        <CardTitle id="timeline-title">Timeline</CardTitle>
        <CardDescription>The incident stream, in order.</CardDescription>
      </CardHeader>
      <CardContent>
        <ol className="flex flex-col gap-3" aria-label="Incident timeline">
          {(incident.timeline ?? []).map((entry) => (
            <li key={entry.streamVersion} className="flex flex-col gap-0.5 border-l border-border-subtle pl-3">
              <span className="text-xs text-text-muted">
                {formatTime(entry.occurredAt)} · v{entry.streamVersion}
              </span>
              <span className="text-sm text-text-primary">{entry.summary}</span>
            </li>
          ))}
        </ol>
      </CardContent>
    </Card>
  );
}

function InvestigationsSection({ incident }: { incident: IncidentDetail }) {
  const investigations = incident.investigations ?? [];
  return (
    <Card aria-labelledby="investigations-title">
      <CardHeader>
        <CardTitle id="investigations-title">Investigations</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 text-sm">
        {investigations.map((investigation) => (
          <div key={investigation.investigationId} className="flex flex-col gap-0.5">
            <span className="text-text-primary">{investigation.outcome ?? "running"}</span>
            <span className="text-text-muted">
              {investigation.model ? `${investigation.model.provider}/${investigation.model.model} · prompt ${investigation.model.promptVersion}` : "Waiting for AgentWorker"}
            </span>
            {investigation.abstentionReason ? <span className="text-text-muted">{investigation.abstentionReason}</span> : null}
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
