import { cn } from "cn"

import { Progress } from "../primitives/progress"

type Calibration = "calibrated" | "uncalibrated"

// Phase 05: confidence is never shown without its evidence completeness and calibration context,
// so all three are required props.
function ConfidenceDisplay({
  confidence,
  evidenceCompleteness,
  calibration,
  className,
}: {
  confidence: number
  evidenceCompleteness: number
  calibration: Calibration
  className?: string
}) {
  const confidencePercent = toPercent(confidence)
  const completenessPercent = toPercent(evidenceCompleteness)

  return (
    <div data-slot="confidence-display" className={cn("flex min-w-48 flex-col gap-1.5", className)}>
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-sm font-medium text-text-primary">{confidencePercent}% confidence</span>
        <span className="text-xs text-text-muted">
          {calibration === "calibrated" ? "Calibrated" : "Uncalibrated"}
        </span>
      </div>
      <Progress
        value={confidencePercent}
        aria-label="Confidence"
        aria-valuetext={`${confidencePercent} percent confidence`}
      />
      <span className="text-xs text-text-muted">Evidence {completenessPercent}% complete</span>
    </div>
  )
}

function toPercent(value: number) {
  return Math.round(Math.min(Math.max(value, 0), 1) * 100)
}

export { ConfidenceDisplay, type Calibration }
