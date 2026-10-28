import { ChevronDown, ChevronsUp, ChevronUp, Equal } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

type Severity = "critical" | "high" | "medium" | "low"

const severities: Record<Severity, StatusPresentation> = {
  critical: { label: "Critical", tone: "critical", icon: ChevronsUp },
  high: { label: "High", tone: "warning", icon: ChevronUp },
  medium: { label: "Medium", tone: "neutral", icon: Equal },
  low: { label: "Low", tone: "neutral", icon: ChevronDown },
}

function SeverityIndicator({ severity, className }: { severity: Severity; className?: string }) {
  const { label, tone, icon } = severities[severity]

  return (
    <StatusBadge tone={tone} icon={icon} className={className}>
      <span className="sr-only">Severity: </span>
      {label}
    </StatusBadge>
  )
}

export { SeverityIndicator, severities, type Severity }
