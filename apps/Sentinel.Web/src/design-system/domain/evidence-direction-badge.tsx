import { CircleDot, CircleMinus, CirclePlus } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "./status-badge"

// Mirrors the evidence direction in architecture §15.
type EvidenceDirection = "supports" | "contradicts" | "neutral"

const evidenceDirections: Record<EvidenceDirection, StatusPresentation> = {
  supports: { label: "Supports", tone: "supporting", icon: CirclePlus },
  contradicts: { label: "Contradicts", tone: "contradicting", icon: CircleMinus },
  neutral: { label: "Neutral", tone: "neutral", icon: CircleDot },
}

function EvidenceDirectionBadge({
  direction,
  className,
}: {
  direction: EvidenceDirection
  className?: string
}) {
  const { label, tone, icon } = evidenceDirections[direction]

  return (
    <StatusBadge tone={tone} icon={icon} className={className}>
      {label}
    </StatusBadge>
  )
}

export { EvidenceDirectionBadge, evidenceDirections, type EvidenceDirection }
