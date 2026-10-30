import * as React from "react"
import { cn } from "@/lib/utils"

// Multi-line text entry, for example a rejection reason. Invalid state is announced through aria-invalid.
function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "flex min-h-20 w-full rounded-md border border-border-subtle bg-surface-canvas px-3 py-2 text-sm text-text-primary placeholder:text-text-muted",
        "focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
        "disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-incident-critical",
        className
      )}
      {...props}
    />
  )
}

export { Textarea }
