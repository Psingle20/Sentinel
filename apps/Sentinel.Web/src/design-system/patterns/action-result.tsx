import * as React from "react"
import { cn } from "@/lib/utils"

// What a server action reports once it has run. A success is a status, read when the reader is free; a failure is an
// alert, read at once. Nothing renders while the action has not run.
type ActionOutcome = { status: "idle" | "done" | "error"; message?: string }

function ActionResult({ state, className }: { state: ActionOutcome; className?: string }) {
  if (state.status === "idle" || !state.message) {
    return null
  }

  return (
    <p role={state.status === "error" ? "alert" : "status"} data-slot="action-result" className={cn("text-sm text-text-primary", className)}>
      {state.message}
    </p>
  )
}

export { ActionResult, type ActionOutcome }
