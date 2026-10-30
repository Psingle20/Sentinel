"use client"

import * as React from "react"

import { applyTheme, readTheme, storeTheme, subscribeTheme, themeChoices, type ThemeChoice } from "@/lib/theme"
import { cn } from "@/lib/utils"

import { Select } from "../primitives/select"

// The responder's theme choice, in the shell's header. The server renders the default; the browser shows the stored
// choice, and a system choice follows the system's preference as it changes.
function ThemeSelect({ className }: { className?: string }) {
  const choice = React.useSyncExternalStore<ThemeChoice>(subscribeTheme, readTheme, () => "dark")

  React.useEffect(() => {
    if (choice !== "system") {
      return
    }

    const media = window.matchMedia("(prefers-color-scheme: dark)")
    const follow = () => applyTheme("system")
    media.addEventListener("change", follow)
    return () => media.removeEventListener("change", follow)
  }, [choice])

  return (
    <Select
      aria-label="Theme"
      value={choice}
      onChange={(event) => {
        const next = event.target.value as ThemeChoice
        storeTheme(next)
        applyTheme(next)
      }}
      className={cn("h-8 w-auto", className)}
    >
      {themeChoices.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </Select>
  )
}

export { ThemeSelect }
