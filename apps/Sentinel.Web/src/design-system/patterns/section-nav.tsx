import * as React from "react"
import { cn } from "cn"

// Navigation between the pages of one section, such as the control plane (Phase 08). The current
// page is marked with aria-current; links are passed in so the pattern stays router-neutral.
function SectionNav({ label, children, className }: { label: string; children: React.ReactNode; className?: string }) {
  return (
    <nav aria-label={label} className={cn("flex flex-wrap gap-1 border-b border-border-subtle", className)}>
      {children}
    </nav>
  )
}

function sectionNavLinkClass(current: boolean) {
  return cn(
    "-mb-px border-b-2 px-3 py-2 text-sm underline-offset-4 focus-visible:underline",
    current ? "border-action-executing font-medium text-text-primary" : "border-transparent text-text-muted hover:text-text-primary"
  )
}

export { SectionNav, sectionNavLinkClass }
