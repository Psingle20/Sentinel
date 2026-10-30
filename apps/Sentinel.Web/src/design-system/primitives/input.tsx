import * as React from "react"
import { cn } from "@/lib/utils"

// Single-line entry, for example a setting's duration or threshold. Invalid state is announced through aria-invalid.
function Input({ className, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      data-slot="input"
      className={cn(
        "flex h-9 w-full rounded-md border border-border-subtle bg-surface-canvas px-3 py-1 text-sm text-text-primary placeholder:text-text-muted",
        "focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
        "disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-incident-critical",
        className
      )}
      {...props}
    />
  )
}

export { Input }
