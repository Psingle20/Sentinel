import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import type { LucideIcon } from "lucide-react"

// Every operational state renders as icon + label + tone, so color is never the only signal.
const statusBadgeVariants = cva(
  "inline-flex h-6 w-fit shrink-0 items-center gap-1.5 rounded-md border px-2 text-xs font-medium whitespace-nowrap [&>svg]:size-3.5 [&>svg]:shrink-0",
  {
    variants: {
      tone: {
        critical:
          "border-incident-critical/40 bg-incident-critical-subtle text-incident-critical",
        warning:
          "border-incident-warning/40 bg-incident-warning-subtle text-incident-warning",
        stable:
          "border-incident-stable/40 bg-incident-stable-subtle text-incident-stable",
        supporting:
          "border-evidence-supporting/40 bg-evidence-supporting-subtle text-evidence-supporting",
        contradicting:
          "border-evidence-contradicting/40 bg-evidence-contradicting-subtle text-evidence-contradicting",
        neutral:
          "border-evidence-neutral/40 bg-evidence-neutral-subtle text-evidence-neutral",
        leading:
          "border-hypothesis-leading/40 bg-hypothesis-leading-subtle text-hypothesis-leading",
        approval:
          "border-approval-required/40 bg-approval-required-subtle text-approval-required",
        executing:
          "border-action-executing/40 bg-action-executing-subtle text-action-executing",
        improving:
          "border-recovery-improving/40 bg-recovery-improving-subtle text-recovery-improving",
        failed:
          "border-recovery-failed/40 bg-recovery-failed-subtle text-recovery-failed",
      },
    },
  }
)

type StatusTone = NonNullable<VariantProps<typeof statusBadgeVariants>["tone"]>

type StatusPresentation = {
  label: string
  tone: StatusTone
  icon: LucideIcon
}

function StatusBadge({
  tone,
  icon: Icon,
  className,
  children,
  ...props
}: React.ComponentProps<"span"> & { tone: StatusTone; icon: LucideIcon }) {
  return (
    <span
      data-slot="status-badge"
      data-tone={tone}
      className={cn(statusBadgeVariants({ tone }), className)}
      {...props}
    >
      <Icon aria-hidden="true" />
      {children}
    </span>
  )
}

export { StatusBadge, statusBadgeVariants, type StatusPresentation, type StatusTone }
