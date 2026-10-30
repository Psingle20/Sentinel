"use client";

import { useState, useTransition } from "react";

import { ActionResult } from "@/design-system/patterns/action-result";
import { Button } from "@/design-system/primitives/button";
import type { TimelineEntry } from "@/lib/api/contracts";

import { olderTimelineAction } from "./actions";
import { formatTime } from "./presentation";

// The incident stream, in order, bounded (Phase 08): the latest page arrives with the page and older pages come from
// Api's paged timeline on request, so a long incident never puts its whole stream in the browser. A live refresh brings
// a new latest page; entries already loaded stay.
export function TimelineList({ incidentId, latest, total, next }: { incidentId: string; latest: TimelineEntry[]; total: number; next?: string }) {
  const [loaded, setLoaded] = useState<TimelineEntry[]>(latest);
  const [shownLatest, setShownLatest] = useState(latest);
  const [cursor, setCursor] = useState(next);
  const [error, setError] = useState<string>();
  const [loading, startLoading] = useTransition();

  // A refresh moves the latest page forward; what was on the previous one stays, so no entry falls out between pages.
  if (latest !== shownLatest) {
    setShownLatest(latest);
    setLoaded((current) => merge(current, latest));
  }

  const entries = merge(loaded, latest);
  const remaining = total - entries.length;

  const loadOlder = () =>
    startLoading(async () => {
      if (cursor === undefined) {
        return;
      }

      const page = await olderTimelineAction(incidentId, cursor);
      if (page.status === "error") {
        setError(page.message);
        return;
      }

      setError(undefined);
      setLoaded((current) => merge(current, page.items));
      setCursor(page.next);
    });

  return (
    <div className="flex flex-col gap-3">
      {cursor !== undefined && remaining > 0 ? (
        <Button variant="outline" size="sm" className="w-fit" onClick={loadOlder} disabled={loading}>
          {loading ? "Loading earlier entries…" : `Show earlier entries (${remaining} more)`}
        </Button>
      ) : null}
      <ActionResult state={error ? { status: "error", message: error } : { status: "idle" }} />
      <ol className="flex flex-col gap-3" aria-label="Incident timeline">
        {entries.map((entry) => (
          <li key={entry.streamVersion} className="flex flex-col gap-0.5 border-l border-border-subtle pl-3">
            <span className="text-caption text-text-muted">
              {formatTime(entry.occurredAt)} · v{entry.streamVersion}
            </span>
            <span className="text-body text-text-primary">{entry.summary}</span>
          </li>
        ))}
      </ol>
      {total > entries.length ? <p className="text-caption text-text-muted">Showing the latest {entries.length} of {total} entries.</p> : null}
    </div>
  );
}

// Entries by stream version, oldest first, each once.
function merge(current: TimelineEntry[], added: TimelineEntry[]): TimelineEntry[] {
  return [...new Map([...current, ...added].map((entry) => [entry.streamVersion, entry])).values()].sort((left, right) => left.streamVersion - right.streamVersion);
}
