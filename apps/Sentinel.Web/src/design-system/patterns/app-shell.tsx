import * as React from "react"
import { cn } from "@/lib/utils"
import { ShieldHalf } from "lucide-react"

import { ThemeSelect } from "./theme-select"

// The responder shell every screen renders inside: product header, optional navigation, theme choice and the main
// region. Below the tablet width the navigation folds into a menu, so the header never wraps.
function AppShell({
  navigation,
  className,
  children,
}: {
  navigation?: React.ReactNode
  className?: string
  children: React.ReactNode
}) {
  return (
    <div
      data-slot="app-shell"
      className={cn("flex min-h-dvh flex-col bg-surface-canvas text-text-primary", className)}
    >
      <a
        href="#main"
        className="sr-only rounded-md bg-surface-panel px-3 py-2 text-sm focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-toast"
      >
        Skip to content
      </a>
      <header className="flex h-12 shrink-0 items-center gap-6 border-b border-border-subtle bg-surface-panel px-4">
        <span className="flex items-center gap-2 text-sm font-semibold tracking-tight">
          <ShieldHalf aria-hidden="true" className="size-4 text-action-executing" />
          Sentinel
        </span>
        {navigation ? (
          <nav aria-label="Primary" className="hidden md:block">
            {navigation}
          </nav>
        ) : null}
        <div className="ml-auto flex items-center gap-2">
          {navigation ? (
            <details className="relative md:hidden">
              <summary className="cursor-pointer list-none rounded-md px-2 py-1 text-sm text-text-muted hover:text-text-primary focus-visible:text-text-primary">
                Menu
              </summary>
              <nav
                aria-label="Primary"
                className="absolute top-full right-0 z-popover mt-2 flex min-w-40 flex-col gap-2 rounded-md border border-border-subtle bg-surface-panel p-3 shadow-overlay"
              >
                {navigation}
              </nav>
            </details>
          ) : null}
          <ThemeSelect />
        </div>
      </header>
      <main id="main" className="flex flex-1 flex-col gap-6 px-6 py-6">
        {children}
      </main>
    </div>
  )
}

export { AppShell }
