import * as React from "react"
import { cn } from "@/lib/utils"

// A choice among a few fixed options, for example an autonomy mode. A native select, so it is labelled, keyboard
// operable and announced like any other control on every platform; invalid state is announced through aria-invalid.
function Select({ className, children, ...props }: React.ComponentProps<"select">) {
  return (
    <select
      data-slot="select"
      className={cn(
        "flex h-9 w-full rounded-md border border-border-subtle bg-surface-canvas px-3 py-1 text-sm text-text-primary",
        "focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none",
        "disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-incident-critical",
        className
      )}
      {...props}
    >
      {children}
    </select>
  )
}

export { Select }
