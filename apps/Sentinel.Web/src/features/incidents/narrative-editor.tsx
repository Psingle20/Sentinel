"use client";

import { useActionState, useId } from "react";

import { Button } from "@/design-system/primitives/button";
import { Textarea } from "@/design-system/primitives/textarea";

import { type NarrativeState, saveNarrativeAction } from "./actions";

// The one part of a postmortem people write (Phase 07). A save names the version it started
// from, so two responders editing at once can never overwrite each other silently.
export function NarrativeEditor({ incidentId, markdown, version }: { incidentId: string; markdown: string; version: number }) {
  const [state, save, saving] = useActionState<NarrativeState, FormData>(saveNarrativeAction, { status: "idle", version });
  const fieldId = useId();

  return (
    <form action={save} className="flex flex-col gap-3">
      <input type="hidden" name="incidentId" value={incidentId} />
      <input type="hidden" name="expectedVersion" value={state.version ?? version} />
      <label htmlFor={fieldId} className="text-sm font-medium text-text-primary">
        Narrative
      </label>
      <Textarea id={fieldId} name="markdown" defaultValue={markdown} rows={10} placeholder="What happened, why, and what we change so it does not happen again." />
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Saving…" : "Save narrative"}
        </Button>
        <span className="text-xs text-text-muted">Version {state.version ?? version}</span>
      </div>
      {state.status !== "idle" ? (
        <p role={state.status === "error" ? "alert" : "status"} className="text-sm text-text-primary">
          {state.message}
        </p>
      ) : null}
    </form>
  );
}
