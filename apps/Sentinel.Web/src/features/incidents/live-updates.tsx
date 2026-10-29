"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";

import { LiveIndicator, type LiveState } from "@/design-system/patterns/live-indicator";
import { track } from "@/features/analytics/track";
import type { LiveEvent } from "@/lib/api/contracts";

const offlinePollMs = 15_000;
const maxBackoffMs = 30_000;

// Live updates (Phase 08). Each event names what changed; the page refetches from the query API,
// which stays the source of truth, so a missed event is never lost state. The browser reconnects
// with Last-Event-ID; when it gives up, this reconnects with backoff from the last event seen, and
// while offline it falls back to polling so the page never silently goes stale.
export function LiveUpdates({ incidentId }: { incidentId?: string }) {
  const router = useRouter();
  const [state, setState] = useState<LiveState>("reconnecting");
  const connected = useRef(false);

  // Counts changes of the connection once it has first been live, so every page load is not a reconnect.
  useEffect(() => {
    if (connected.current) {
      track("live.state-changed", { state });
    } else if (state === "live") {
      connected.current = true;
    }
  }, [state]);

  useEffect(() => {
    let source: EventSource | undefined;
    let lastEventId = "";
    let failures = 0;
    let refreshTimer: number | undefined;
    let retryTimer: number | undefined;
    let pollTimer: number | undefined;
    let stopped = false;

    const refresh = () => {
      window.clearTimeout(refreshTimer);
      refreshTimer = window.setTimeout(() => router.refresh(), 300);
    };

    const connect = () => {
      source = new EventSource(lastEventId ? `/api/events?after=${encodeURIComponent(lastEventId)}` : "/api/events");
      source.onopen = () => {
        failures = 0;
        window.clearInterval(pollTimer);
        setState("live");
        refresh();
      };
      source.addEventListener("incident", (event) => {
        const message = event as MessageEvent<string>;
        lastEventId = message.lastEventId || lastEventId;
        const live = JSON.parse(message.data) as LiveEvent;
        if (!incidentId || live.incidentId === incidentId) {
          refresh();
        }
      });
      source.onerror = () => {
        failures += 1;
        if (source?.readyState !== EventSource.CLOSED) {
          setState("reconnecting");
          return;
        }

        setState(failures > 3 ? "offline" : "reconnecting");
        if (failures > 3) {
          window.clearInterval(pollTimer);
          pollTimer = window.setInterval(() => router.refresh(), offlinePollMs);
        }

        source.close();
        if (!stopped) {
          retryTimer = window.setTimeout(connect, Math.min(maxBackoffMs, 1000 * 2 ** failures));
        }
      };
    };

    connect();
    return () => {
      stopped = true;
      window.clearTimeout(refreshTimer);
      window.clearTimeout(retryTimer);
      window.clearInterval(pollTimer);
      source?.close();
    };
  }, [router, incidentId]);

  return <LiveIndicator state={state} />;
}
