import { Activity, CircleCheck, CircleHelp, Minus, TrendingDown, TrendingUp, Undo2 } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

// Recovery outcomes from Phase 06 and ADR-006. "inconclusive" covers stale or incomplete telemetry.
type RecoveryStatus =
  | "verifying"
  | "improving"
  | "recovered"
  | "unchanged"
  | "degraded"
  | "inconclusive"
  | "compensation-required"

const recoveryStatuses: Record<RecoveryStatus, StatusPresentation> = {
  verifying: { label: "Verifying", tone: "executing", icon: Activity },
  improving: { label: "Improving", tone: "improving", icon: TrendingUp },
  recovered: { label: "Recovered", tone: "stable", icon: CircleCheck },
  unchanged: { label: "Unchanged", tone: "warning", icon: Minus },
  degraded: { label: "Degraded", tone: "failed", icon: TrendingDown },
  inconclusive: { label: "Inconclusive", tone: "neutral", icon: CircleHelp },
  "compensation-required": { label: "Compensation required", tone: "failed", icon: Undo2 },
}

function RecoveryState({ status, className }: { status: RecoveryStatus; className?: string }) {
  const { label, tone, icon } = recoveryStatuses[status]

  return (
    <StatusBadge tone={tone} icon={icon} className={className}>
      {label}
    </StatusBadge>
  )
}

export { RecoveryState, recoveryStatuses, type RecoveryStatus }
