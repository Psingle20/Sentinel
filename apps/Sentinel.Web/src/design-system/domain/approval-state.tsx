import { Ban, Hourglass, Lightbulb, Minus, ShieldAlert, ShieldCheck, ShieldOff, ShieldX } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

// Approval lifecycle from Phase 06: requested, granted, rejected, expired or revoked. A suggestion
// is never executed by Sentinel and a denied plan cannot be approved, so neither reads as "not required".
type ApprovalStatus = "not-required" | "required" | "approved" | "rejected" | "expired" | "revoked" | "suggestion-only" | "denied"

const approvalStatuses: Record<ApprovalStatus, StatusPresentation> = {
  "not-required": { label: "Approval not required", tone: "neutral", icon: Minus },
  required: { label: "Approval required", tone: "approval", icon: ShieldAlert },
  approved: { label: "Approved", tone: "stable", icon: ShieldCheck },
  rejected: { label: "Rejected", tone: "critical", icon: ShieldX },
  expired: { label: "Expired", tone: "neutral", icon: Hourglass },
  revoked: { label: "Revoked", tone: "critical", icon: Ban },
  "suggestion-only": { label: "Suggestion only: a person acts", tone: "neutral", icon: Lightbulb },
  denied: { label: "Denied by policy", tone: "critical", icon: ShieldOff },
}

function ApprovalState({ status, className }: { status: ApprovalStatus; className?: string }) {
  const { label, tone, icon } = approvalStatuses[status]

  return (
    <StatusBadge tone={tone} icon={icon} className={className}>
      {label}
    </StatusBadge>
  )
}

export { ApprovalState, approvalStatuses, type ApprovalStatus }
