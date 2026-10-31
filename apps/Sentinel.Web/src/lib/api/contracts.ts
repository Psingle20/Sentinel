// Sentinel.Gateway's HTTP contract as the web app reads it. It mirrors the views in
// packages/Sentinel.Contracts (camelCase JSON, enums as camelCase strings, nulls omitted) and is
// maintained by hand beside docs/business-contracts/synchronous-contracts.md; Api's OpenAPI
// document at /openapi/v1.json is the reference when they disagree.

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
  symptomOnset?: string;
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
  key?: string;
};

export type HypothesisStatus = "proposed" | "supported" | "challenged" | "rejected" | "confirmed" | "superseded";

export type HypothesisView = {
  finding: HypothesisFinding;
  investigationId: string;
  status: HypothesisStatus;
  lineage?: string[];
  proposedBy?: string[];
  refines?: string;
};

export type DecisionCheck = { name: string; passed: boolean; detail: string };

export type HypothesisAssessment = {
  hypothesisId: string;
  score: number;
  independentSupport: number;
  independentContradiction: number;
  eliminated: boolean;
  reason: string;
};

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
  dissentingHypothesisIds?: string[];
  assessments?: HypothesisAssessment[];
  missingEvidence?: string[];
  algorithmVersion?: string;
  inputsHash?: string;
  supersededBy?: string;
  // Phase E05: why an abstention abstained, and the two explanations an ambiguous one could not choose between.
  abstainedFor?: "ambiguous" | "insufficient-evidence" | "falsified";
  contenders?: string[];
};

export type RollbackAction = {
  kind: "rollback";
  actionId: string;
  targetServiceId: string;
  fromRevision: string;
  toRevision: string;
};

export type PauseBatchAction = {
  kind: "pause-batch";
  actionId: string;
  targetServiceId: string;
  reason: string;
};

export type QuarantineMessageAction = {
  kind: "quarantine-message";
  actionId: string;
  targetServiceId: string;
  queue: string;
  minimumReceives: number;
  maxMessages: number;
  reason: string;
};

export type RestoreRouteAction = {
  kind: "restore-route";
  actionId: string;
  targetServiceId: string;
  rule: string;
  eventBus: string;
  reason: string;
};

export type RemediationAction = RollbackAction | PauseBatchAction | QuarantineMessageAction | RestoreRouteAction;

// Every action kind Executor has an adapter for; an operator can take each one out of service.
export const remediationActionKinds: readonly RemediationAction["kind"][] = ["rollback", "pause-batch", "quarantine-message", "restore-route"];

export type PlanPrecondition = { serviceId: string; check: string; expected: string };

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
  preconditions?: PlanPrecondition[];
  expectedEffect?: string;
  blastRadius?: string[];
  compensation?: string;
};

export type PolicyEvaluation = {
  decision: "allow" | "deny" | "approvalRequired" | "suggestionOnly";
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
  // When an unanswered approval request expires and the incident escalates (Phase 08).
  approvalDeadline?: string;
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

// baselineValue is the signal's average over an equal window before the action (Phase 08), absent on older incidents.
export type RecoveryMeasurement = { criterion: RecoveryCriterion; observedValue?: number; met?: boolean; baselineValue?: number };

export type RecoveryView = {
  planId: string;
  outcome: RecoveryOutcome;
  measurements: RecoveryMeasurement[];
  observedThrough: string;
  evaluatedAt: string;
  reason?: string;
};

// Phase E03: a remedy an earlier cycle executed and verified at its target, with the recovery verdict that followed.
export type RemedyTried = {
  cycle: number;
  planId: string;
  targetServiceId: string;
  summary: string;
  outcome: RecoveryOutcome;
  measurements: RecoveryMeasurement[];
  reason?: string;
  judgedAt: string;
  followedUp: boolean;
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
  role?: string;
  timedOut?: boolean;
  // Phase E05: the escalation investigator's one discriminating query, as a sentence.
  query?: string;
};

export type LinkedChangeView = {
  deploymentId: string;
  serviceId: string;
  changeKind: string;
  version: string;
  previousVersion?: string;
  startedAt: string;
  servingFrom?: string;
  failed: boolean;
  score?: number;
  reasons: string[];
  observationIds: string[];
};

export type LinkedSignalView = { serviceId: string; metricName: string; value: number; score: number; reasons: string[]; linkedAt: string };

export type ChallengeView = {
  hypothesisId: string;
  falsifier: string;
  verdict: "survived" | "falsified" | "inconclusive";
  detail: string;
  evidenceIds: string[];
  investigationId: string;
};

export type CausalNodeKind = "symptom" | "service" | "change" | "hypothesis" | "evidence";

export type CausalNode = { id: string; kind: CausalNodeKind; label: string };

export type CausalEdge = { from: string; to: string; relation: string; weight?: number };

export type CausalGraphView = { nodes: CausalNode[]; edges: CausalEdge[] };

// Text in untrusted tool results that read as an instruction to an AI agent: shown, never obeyed.
export type SuspectedInjection = {
  tool: string;
  source: string;
  serviceId?: string;
  excerpt: string;
  pattern: string;
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
  affectedServices?: string[];
  changes?: LinkedChangeView[];
  linkedSignals?: LinkedSignalView[];
  challenges?: ChallengeView[];
  decisions?: DecisionView[];
  cycle?: number;
  causalGraph?: CausalGraphView;
  suspectedInjections?: SuspectedInjection[];
  remediesTried?: RemedyTried[];
};

// Phase 07: memory, replay and postmortems.

// Phase 07's run record as the Gateway exports it: the incident's projection with its version manifest. Judge mode
// (Phase E04) plays it as a captured story.
export type IncidentRunRecord = {
  schemaVersion: number;
  incident: IncidentDetail;
  exportedAt: string;
  versions?: VersionManifest;
};

export type SimilarIncident = {
  incidentId: string;
  similarity: number;
  sharedFeatures: string[];
  causeKey?: string;
  causeStatement?: string;
  outcome: string;
  recordedAt: string;
  actionSummary?: string;
};

export type SourceRef = { kind: string; ref: string };

export type PostmortemFact = { text: string; sources: SourceRef[]; at?: string };

export type PostmortemNarrative = { markdown: string; version: number; editedBy: string; editedAt: string };

export type VersionManifest = {
  schemaVersion: number;
  decisionAlgorithm?: string;
  models?: ModelMetadata[];
  toolCatalogVersions?: string[];
  policyVersion?: string;
  redactionPolicyVersion?: string;
  dataset?: string;
};

export type Postmortem = {
  incidentId: string;
  title: string;
  status: IncidentStatus;
  streamVersion: number;
  generatedAt: string;
  summary: PostmortemFact;
  timeline?: PostmortemFact[];
  evidence?: PostmortemFact[];
  decision?: PostmortemFact[];
  actions?: PostmortemFact[];
  recovery?: PostmortemFact[];
  openQuestions?: PostmortemFact[];
  versions: VersionManifest;
  narrative?: PostmortemNarrative;
};

export type DecisionReplay = {
  decisionId: string;
  streamVersion: number;
  algorithmVersion?: string;
  settingsRecorded: boolean;
  recordedOutcome: "diagnosed" | "abstained";
  replayedOutcome: "diagnosed" | "abstained";
  recordedConfidence: number;
  replayedConfidence: number;
  matches: boolean;
};

export type DomainReplayResult = {
  level: "domain";
  deterministic: boolean;
  incidentId: string;
  streamVersion: number;
  eventCount: number;
  stateHash: string;
  stateStable: boolean;
  projectionMatches: boolean;
  projectionDifferences?: string[];
  decisions?: DecisionReplay[];
  replayedAt: string;
};

export type ToolCallRecord = { toolCallId: string; toolName: string; partial?: boolean; error?: string; startedAt: string; duration: string };

// AgentWorker's execution audit record, as Api passes it through. Findings are omitted from views.
export type AgentExecution = {
  agentExecutionId: string;
  incidentId: string;
  investigationId: string;
  investigator: string;
  shadow: boolean;
  modelProvider: string;
  model: string;
  modelVersion?: string;
  promptVersion: string;
  result: "completed" | "abstained" | "failed";
  abstentionReason?: string;
  toolCalls?: ToolCallRecord[];
  modelCalls: number;
  inputTokens: number;
  outputTokens: number;
  schemaFailures: number;
  startedAt: string;
  completedAt: string;
  // The findings in contract JSON, as sent or, for the shadow baseline, as they would have been sent.
  findings?: string;
};

// Phase 08: live updates and the control plane.

export type LiveEvent = {
  sequence: number;
  tenantId: string;
  environmentId: string;
  incidentId: string;
  kind: string;
  streamVersion: number;
  summary: string;
  severity?: Severity;
  occurredAt: string;
};

export type PolicyVersionInfo = { version: string; author: string; publishedAt: string; source: "file" | "control-plane" };

export type ExecutorControls = { emergencyStop: boolean; disabledActions: string[] };

export type AuditEntry = { sequence: number; actor: string; action: string; target: string; detail: unknown; occurredAt: string };

// One Ingestion source as operators see it (Phase 02): how far its data is complete and how its last pulls went.
export type SourceState = {
  sourceId: string;
  provider: string;
  watermark: string;
  health: "Healthy" | "Degraded" | "Unavailable";
  lastError?: string;
  consecutiveFailures: number;
  healthy: boolean;
  lastPulledAt: string;
  nextAttemptAt?: string;
};

// Configuration areas (Phase 08): each document is owned and validated by one service.
export const configurationAreas = ["runtime", "models", "sources", "api"] as const;

export type ConfigurationArea = (typeof configurationAreas)[number];

export type DocumentVersion = { version: string; author: string; publishedAt: string; basedOn?: string | null };

// The author of the version a service starts from, its deployment configuration; history always ends with it.
export const seedAuthor = "deployment configuration";

export type DocumentState = { document: Record<string, unknown>; version: DocumentVersion; published: boolean };

export type AnalyticsEvent = { name: string; properties?: Record<string, string> };

export type AnalyticsCount = { day: string; event: string; dimensions: string; count: number };

export type Page<T> = { items: T[]; total: number; next?: string };

export type ApprovalReceipt = {
  approvalId: string;
  decision: "approved" | "rejected";
  attestationId?: string;
  decidedAt: string;
  expiresAt?: string;
};

export type ProblemDetails = { title?: string; detail?: string; status?: number; code?: string };
