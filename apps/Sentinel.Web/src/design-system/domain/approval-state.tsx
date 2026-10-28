import { Ban, Hourglass, Minus, ShieldAlert, ShieldCheck, ShieldX } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

// Approval lifecycle from Phase 06: requested, granted, rejected, expired or revoked.
type ApprovalStatus = "not-required" | "required" | "approved" | "rejected" | "expired" | "revoked"

const approvalStatuses: Record<ApprovalStatus, StatusPresentation> = {
  "not-required": { label: "Approval not required", tone: "neutral", icon: Minus },
  required: { label: "Approval required", tone: "approval", icon: ShieldAlert },
  approved: { label: "Approved", tone: "stable", icon: ShieldCheck },
  rejected: { label: "Rejected", tone: "critical", icon: ShieldX },
  expired: { label: "Expired", tone: "neutral", icon: Hourglass },
  revoked: { label: "Revoked", tone: "critical", icon: Ban },
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
