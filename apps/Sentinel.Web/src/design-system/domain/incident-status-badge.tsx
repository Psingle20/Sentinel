import { Activity, CircleCheck, Search, ShieldAlert, Siren, TriangleAlert, Wrench } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

type IncidentStatus =
  | "detected"
  | "investigating"
  | "awaiting-approval"
  | "remediating"
  | "verifying"
  | "resolved"
  | "escalated"

const incidentStatuses: Record<IncidentStatus, StatusPresentation> = {
  detected: { label: "Detected", tone: "warning", icon: Siren },
  investigating: { label: "Investigating", tone: "executing", icon: Search },
  "awaiting-approval": { label: "Awaiting approval", tone: "approval", icon: ShieldAlert },
  remediating: { label: "Remediating", tone: "executing", icon: Wrench },
  verifying: { label: "Verifying recovery", tone: "improving", icon: Activity },
  resolved: { label: "Resolved", tone: "stable", icon: CircleCheck },
  escalated: { label: "Escalated", tone: "critical", icon: TriangleAlert },
}

function IncidentStatusBadge({ status, className }: { status: IncidentStatus; className?: string }) {
  const { label, tone, icon } = incidentStatuses[status]

  return (
    <StatusBadge tone={tone} icon={icon} className={className}>
      {label}
    </StatusBadge>
  )
}

export { IncidentStatusBadge, incidentStatuses, type IncidentStatus }
