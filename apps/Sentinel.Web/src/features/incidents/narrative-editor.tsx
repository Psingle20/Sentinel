"use client";

import { useActionState, useEffect } from "react";

import { ActionResult } from "@/design-system/patterns/action-result";
import { Field } from "@/design-system/patterns/field";
import { Button } from "@/design-system/primitives/button";
import { Textarea } from "@/design-system/primitives/textarea";
import { track } from "@/features/analytics/track";

import { type NarrativeState, saveNarrativeAction } from "./actions";

// The one part of a postmortem people write (Phase 07). A save names the version it started
// from, so two responders editing at once can never overwrite each other silently.
export function NarrativeEditor({ incidentId, markdown, version }: { incidentId: string; markdown: string; version: number }) {
  const [state, save, saving] = useActionState<NarrativeState, FormData>(saveNarrativeAction, { status: "idle", version });

  useEffect(() => {
    if (state.status !== "idle") {
      track("narrative.saved", { outcome: state.status === "done" ? "saved" : state.conflict ? "conflict" : "failed" });
    }
  }, [state]);

  return (
    <form action={save} className="flex flex-col gap-3">
      <input type="hidden" name="incidentId" value={incidentId} />
      <input type="hidden" name="expectedVersion" value={state.version ?? version} />
      <Field label="Narrative">
        {(control) => <Textarea {...control} name="markdown" defaultValue={markdown} rows={10} placeholder="What happened, why, and what we change so it does not happen again." />}
      </Field>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" disabled={saving}>
          {saving ? "Saving…" : "Save narrative"}
        </Button>
        <span className="text-caption text-text-muted tabular-nums">Version {state.version ?? version}</span>
      </div>
      <ActionResult state={state} />
    </form>
  );
}
