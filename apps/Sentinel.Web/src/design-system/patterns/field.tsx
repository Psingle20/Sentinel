"use client"

import * as React from "react"
import { cn } from "@/lib/utils"

import { Label } from "../primitives/label"

// What a control needs from its field: its id, the ids of the text that describes it, and whether it is invalid.
type FieldControlProps = {
  id: string
  "aria-describedby"?: string
  "aria-invalid"?: true
}

type FieldProps = {
  label: React.ReactNode
  description?: React.ReactNode
  error?: React.ReactNode
  // A checkbox's label sits beside it; every other control's label sits above it.
  orientation?: "vertical" | "horizontal"
  className?: string
  children: (control: FieldControlProps) => React.ReactNode
}

// One labelled control with an optional description and error, wired for assistive technology: the label names the
// control, the description and the error describe it, and an error marks it invalid. Feature code composes a Field
// instead of writing a label and its control by hand.
function Field({ label, description, error, orientation = "vertical", className, children }: FieldProps) {
  const id = React.useId()
  const descriptionId = description ? `${id}-description` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [descriptionId, errorId].filter(Boolean).join(" ") || undefined
  const control = children({ id, "aria-describedby": describedBy, ...(error ? { "aria-invalid": true as const } : {}) })

  return (
    <div data-slot="field" data-orientation={orientation} className={cn("flex flex-col gap-1.5", className)}>
      {orientation === "horizontal" ? (
        <div className="flex items-center gap-2">
          {control}
          <Label htmlFor={id} className="font-normal">
            {label}
          </Label>
        </div>
      ) : (
        <>
          <Label htmlFor={id}>{label}</Label>
          {control}
        </>
      )}
      {description ? (
        <p id={descriptionId} className="text-xs text-text-muted">
          {description}
        </p>
      ) : null}
      {error ? (
        <p id={errorId} className="text-xs text-incident-critical">
          {error}
        </p>
      ) : null}
    </div>
  )
}

export { Field, type FieldControlProps }
