import type { ApprovalStatus } from "@/design-system/domain/approval-state";
import type { IncidentStatus as BadgeIncidentStatus } from "@/design-system/domain/incident-status-badge";
import type { RecoveryStatus } from "@/design-system/domain/recovery-state";
import type {
  ExecutionOutcome,
  IncidentStatus,
  PlanView,
  PolicyEvaluation,
  RecoveryCriterion,
  RecoveryOutcome,
  RemediationAction,
} from "@/lib/api/contracts";

// Maps Api's contract values onto the design system's presentation vocabulary.

export function incidentStatus(status: IncidentStatus): BadgeIncidentStatus {
  return status === "awaitingApproval" ? "awaiting-approval" : status;
}

export function approvalStatus(view: Pick<PlanView, "status" | "approvedBy">): ApprovalStatus {
  switch (view.status) {
    case "awaitingApproval":
      return "required";
    case "rejected":
      return "rejected";
    case "suggestionOnly":
      return "suggestion-only";
    case "denied":
      return "denied";
    case "superseded":
      return "expired";
    default:
      // Approved, executing or done: a person approved it, or policy let it run without approval.
      return view.approvedBy ? "approved" : "not-required";
  }
}

const policyVerdicts: Record<PolicyEvaluation["decision"], string> = {
  allow: "allows it without approval",
  approvalRequired: "requires approval",
  suggestionOnly: "allows only a suggestion, which a person carries out",
  deny: "denies it",
};

export function policyVerdict(decision: PolicyEvaluation["decision"]): string {
  return policyVerdicts[decision];
}

export function recoveryStatus(outcome: RecoveryOutcome): RecoveryStatus {
  return outcome === "compensationRequired" ? "compensation-required" : outcome;
}

const executionLabels: Record<ExecutionOutcome, string> = {
  rejected: "Rejected by Executor",
  aborted: "Aborted before any change",
  actionVerified: "Action verified at the target",
  failed: "Failed",
  outcomeUnknown: "Outcome unknown, reconciling",
  compensated: "Compensated",
  compensationFailed: "Compensation failed",
};

export function executionLabel(outcome: ExecutionOutcome | undefined): string {
  return outcome ? executionLabels[outcome] : "Executing";
}

export function describeAction(action: RemediationAction): string {
  switch (action.kind) {
    case "pause-batch":
      return `Pause the ${action.targetServiceId} batch`;
    case "quarantine-message":
      return `Quarantine the messages of ${action.targetServiceId}'s ${action.queue} queue received at least ${action.minimumReceives} times`;
    default:
      return `Roll back ${action.targetServiceId} from ${action.fromRevision} to ${action.toRevision}`;
  }
}

const comparisons: Record<RecoveryCriterion["comparison"], string> = {
  lessThan: "<",
  lessThanOrEqual: "≤",
  greaterThan: ">",
  greaterThanOrEqual: "≥",
};

export function describeCriterion(criterion: RecoveryCriterion): string {
  return `${criterion.serviceId} ${criterion.metricName} ${comparisons[criterion.comparison]} ${criterion.threshold}`;
}

const timeFormat = new Intl.DateTimeFormat("en-GB", {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
  timeZone: "UTC",
});

const dateTimeFormat = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
  timeZone: "UTC",
});

// Event times render in UTC, so every responder reads the same timeline.
export function formatTime(value: string): string {
  return `${timeFormat.format(new Date(value))} UTC`;
}

export function formatDateTime(value: string): string {
  return `${dateTimeFormat.format(new Date(value))} UTC`;
}

export function percent(value: number): string {
  return `${Math.round(value * 100)}%`;
}
