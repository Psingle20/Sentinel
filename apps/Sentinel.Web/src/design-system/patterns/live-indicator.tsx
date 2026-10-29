import { Radio, RefreshCw, WifiOff } from "lucide-react"

import { StatusBadge, type StatusPresentation } from "../domain/status-badge"

// The live-update channel's state (Phase 08). The page stays correct without it: the query API is
// the source of truth, so a reconnecting channel only means updates arrive later.
type LiveState = "live" | "reconnecting" | "offline"

const liveStates: Record<LiveState, StatusPresentation> = {
  live: { label: "Live", tone: "stable", icon: Radio },
  reconnecting: { label: "Reconnecting", tone: "warning", icon: RefreshCw },
  offline: { label: "Updates paused: refresh to catch up", tone: "critical", icon: WifiOff },
}

function LiveIndicator({ state, className }: { state: LiveState; className?: string }) {
  const { label, tone, icon } = liveStates[state]
  return (
    <span role="status" aria-live="polite" className={className}>
      <StatusBadge tone={tone} icon={icon}>
        {label}
      </StatusBadge>
    </span>
  )
}

export { LiveIndicator, liveStates, type LiveState }
