// Sentinel.Api's HTTP contract as the web app reads it. It mirrors the incident views in
// packages/Sentinel.Contracts (camelCase JSON, enums as camelCase strings). Phase 08 replaces
// these hand-written types with types generated from Api's OpenAPI document (ADR-012).

export type IncidentStatus =
  | "detected"
  | "investigating"
  | "awaitingApproval"
  | "remediating"
  | "verifying"
  | "resolved"
  | "escalated";

export type Severity = "critical" | "high" | "medium" | "low";

export type IncidentSummary = {
  tenantId: string;
  environmentId: string;
  incidentId: string;
  affectedServiceId: string;
  title: string;
  severity: Severity;
  status: IncidentStatus;
  openedAt: string;
  updatedAt: string;
  streamVersion: number;
};

export type TimelineEntry = {
  streamVersion: number;
  occurredAt: string;
  kind: string;
  summary: string;
};

export type TimeRange = { from: string; to: string };

export type EvidenceFinding = {
  evidenceId: string;
  statement: string;
  direction: "supports" | "contradicts" | "neutral";
  strength: number;
  sourceObservationIds: string[];
  hypothesisIds?: string[];
  eventTimeRange: TimeRange;
  toolCallId: string;
  independenceGroup?: string;
  partial?: boolean;
};

export type EvidenceView = {
  finding: EvidenceFinding;
  investigationId: string;
  status: "accepted" | "rejected";
  rejectionReason?: string;
};

export type HypothesisFinding = {
  hypothesisId: string;
  statement: string;
  causalChain: string[];
  suspectedServiceId?: string;
  suspectedChangeObservationId?: string;
  supportingEvidenceIds?: string[];
  contradictingEvidenceIds?: string[];
};

export type HypothesisStatus = "proposed" | "supported" | "challenged" | "rejected" | "confirmed" | "superseded";

export type HypothesisView = {
  finding: HypothesisFinding;
  investigationId: string;
  status: HypothesisStatus;
};

export type DecisionCheck = { name: string; passed: boolean; detail: string };

export type DecisionView = {
  decisionId: string;
  outcome: "diagnosed" | "abstained";
  leadingHypothesisId?: string;
  rejectedHypothesisIds?: string[];
  confidence: number;
  evidenceCompleteness: number;
  calibration: "calibrated" | "uncalibrated";
  rationale: string;
  checks?: DecisionCheck[];
  decidedAt: string;
};

export type RollbackAction = {
  kind: "rollback";
  actionId: string;
  targetServiceId: string;
  fromRevision: string;
  toRevision: string;
};

export type RemediationAction = RollbackAction;

export type RecoveryCriterion = {
  serviceId: string;
  metricName: string;
  comparison: "lessThan" | "lessThanOrEqual" | "greaterThan" | "greaterThanOrEqual";
  threshold: number;
};

export type RemediationPlan = {
  planId: string;
  planVersion: number;
  planHash: string;
  hypothesisId: string;
  actions: RemediationAction[];
  expectedRecovery: RecoveryCriterion[];
  observationWindow: string;
  risk: "low" | "medium" | "high";
};

export type PolicyEvaluation = {
  decision: "allow" | "deny" | "approvalRequired";
  policyVersion: string;
  ruleTrace: string[];
  evaluatedAt: string;
};

export type PlanStatus =
  | "awaitingApproval"
  | "suggestionOnly"
  | "denied"
  | "approved"
  | "rejected"
  | "executing"
  | "executed"
  | "failed"
  | "superseded";

export type PlanView = {
  plan: RemediationPlan;
  policyEvaluation: PolicyEvaluation;
  status: PlanStatus;
  approvedBy?: string;
  approvedAt?: string;
};

export type ExecutionOutcome =
  | "rejected"
  | "aborted"
  | "actionVerified"
  | "failed"
  | "outcomeUnknown"
  | "compensated"
  | "compensationFailed";

export type ExecutionView = {
  planId: string;
  planVersion: number;
  outcome?: ExecutionOutcome;
  detail?: string;
  requestedAt: string;
  startedAt?: string;
  completedAt?: string;
};

export type RecoveryOutcome = "recovered" | "unchanged" | "degraded" | "inconclusive" | "compensationRequired";

export type RecoveryMeasurement = { criterion: RecoveryCriterion; observedValue?: number; met?: boolean };

export type RecoveryView = {
  planId: string;
  outcome: RecoveryOutcome;
  measurements: RecoveryMeasurement[];
  observedThrough: string;
  evaluatedAt: string;
  reason?: string;
};

export type ModelMetadata = { provider: string; model: string; modelVersion?: string; promptVersion: string };

export type InvestigationView = {
  investigationId: string;
  requestedAt: string;
  agentExecutionId?: string;
  outcome?: "completed" | "abstained" | "failed";
  model?: ModelMetadata;
  abstentionReason?: string;
  completedAt?: string;
};

export type IncidentDetail = {
  summary: IncidentSummary;
  triggerObservationIds?: string[];
  linkedDeploymentObservationIds?: string[];
  timeline?: TimelineEntry[];
  investigations?: InvestigationView[];
  evidence?: EvidenceView[];
  hypotheses?: HypothesisView[];
  decision?: DecisionView;
  plan?: PlanView;
  execution?: ExecutionView;
  recovery?: RecoveryView;
};

export type ApprovalReceipt = {
  approvalId: string;
  decision: "approved" | "rejected";
  attestationId?: string;
  decidedAt: string;
  expiresAt?: string;
};

export type ProblemDetails = { title?: string; detail?: string; status?: number; code?: string };
