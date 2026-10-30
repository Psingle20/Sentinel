"use client";

import { CircleCheck, CircleX } from "lucide-react";
import { useActionState, useEffect } from "react";

import { StatusBadge } from "@/design-system/domain/status-badge";
import { ActionResult } from "@/design-system/patterns/action-result";
import { Button } from "@/design-system/primitives/button";
import { track } from "@/features/analytics/track";

import { replayAction, type ReplayState } from "./actions";

// Level-1 domain replay (Phase 07): rebuilds the incident from its event stream and re-runs every
// recorded decision. Read-only: nothing is written, sent or executed.
export function ReplayPanel({ incidentId }: { incidentId: string }) {
  const [state, replay, replaying] = useActionState<ReplayState, FormData>(replayAction, { status: "idle" });
  const result = state.result;

  useEffect(() => {
    if (state.status !== "idle") {
      track("replay.requested", { outcome: state.status === "error" ? "failed" : state.reproduced ? "reproduced" : "differs" });
    }
  }, [state]);

  return (
    <form action={replay} className="flex flex-col gap-3">
      <input type="hidden" name="incidentId" value={incidentId} />
      <Button type="submit" variant="outline" className="w-fit" disabled={replaying}>
        {replaying ? "Replaying…" : "Replay from the event stream"}
      </Button>
      <ActionResult state={state} />
      {result ? (
        <ul className="flex flex-col gap-2 text-body" aria-label="Replay checks">
          <Check passed={result.stateStable} label={`State rebuilds identically (${result.stateHash.slice(0, 16)})`} />
          <Check passed={result.projectionMatches} label="The stored projection matches the stream" />
          {(result.projectionDifferences ?? []).map((difference) => (
            <li key={difference} className="font-mono text-caption text-text-muted">
              {difference}
            </li>
          ))}
          {(result.decisions ?? []).map((decision) => (
            <Check
              key={decision.decisionId}
              passed={decision.matches}
              label={`Decision at v${decision.streamVersion} (${decision.algorithmVersion ?? "falsification-v1"}): recorded ${decision.recordedOutcome}, replayed ${decision.replayedOutcome}${decision.settingsRecorded ? "" : ", with current settings"}`}
            />
          ))}
        </ul>
      ) : null}
    </form>
  );
}

function Check({ passed, label }: { passed: boolean; label: string }) {
  return (
    <li>
      <StatusBadge tone={passed ? "stable" : "critical"} icon={passed ? CircleCheck : CircleX}>
        {passed ? "Matches" : "Differs"}
      </StatusBadge>{" "}
      <span className="text-text-primary">{label}</span>
    </li>
  );
}
