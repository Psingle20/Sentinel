import * as React from "react"
import { cva } from "class-variance-authority"
import { cn } from "@/lib/utils"
import { CircleAlert, Clock, Inbox, Lock, type LucideIcon, TriangleAlert } from "lucide-react"

import { Skeleton } from "../primitives/skeleton"

// The non-happy states every data view must cover (frontend design system, contribution contract).
type DataStateKind = "loading" | "empty" | "error" | "stale" | "degraded" | "permission-denied"

const panelVariants = cva(
  "flex flex-col items-center justify-center gap-3 rounded-lg border px-6 py-10 text-center",
  {
    variants: {
      kind: {
        loading: "border-border-subtle bg-surface-panel",
        empty: "border-border-subtle bg-surface-panel",
        error: "border-incident-critical/40 bg-incident-critical-subtle",
        stale: "border-incident-warning/40 bg-incident-warning-subtle",
        degraded: "border-incident-warning/40 bg-incident-warning-subtle",
        "permission-denied": "border-border-subtle bg-surface-panel",
      },
    },
  }
)

const iconVariants = cva("size-6", {
  variants: {
    kind: {
      loading: "text-text-muted",
      empty: "text-text-muted",
      error: "text-incident-critical",
      stale: "text-incident-warning",
      degraded: "text-incident-warning",
      "permission-denied": "text-text-muted",
    },
  },
})

// Muted text loses contrast on tinted panels, so tinted states use primary text for descriptions.
const descriptionVariants = cva("text-sm", {
  variants: {
    kind: {
      loading: "text-text-muted",
      empty: "text-text-muted",
      error: "text-text-primary",
      stale: "text-text-primary",
      degraded: "text-text-primary",
      "permission-denied": "text-text-muted",
    },
  },
})

const icons: Record<Exclude<DataStateKind, "loading">, LucideIcon> = {
  empty: Inbox,
  error: CircleAlert,
  stale: Clock,
  degraded: TriangleAlert,
  "permission-denied": Lock,
}

function DataState({
  kind,
  title,
  description,
  action,
  className,
}: {
  kind: DataStateKind
  title: string
  description?: string
  action?: React.ReactNode
  className?: string
}) {
  // Errors interrupt; the other states are announced politely.
  const role = kind === "error" ? "alert" : "status"

  if (kind === "loading") {
    return (
      <div data-slot="data-state" data-kind={kind} role={role} className={cn(panelVariants({ kind }), className)}>
        <span className="sr-only">{title}</span>
        <div aria-hidden="true" className="flex w-full max-w-md flex-col gap-2">
          <Skeleton className="h-4 w-3/4" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-2/3" />
        </div>
      </div>
    )
  }

  const Icon = icons[kind]

  return (
    <div data-slot="data-state" data-kind={kind} role={role} className={cn(panelVariants({ kind }), className)}>
      <Icon aria-hidden="true" className={iconVariants({ kind })} />
      <div className="flex max-w-md flex-col gap-1">
        <p className="text-sm font-medium text-text-primary">{title}</p>
        {description ? <p className={descriptionVariants({ kind })}>{description}</p> : null}
      </div>
      {action}
    </div>
  )
}

export { DataState, type DataStateKind }
