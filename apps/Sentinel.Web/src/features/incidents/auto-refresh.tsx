"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

// The walking skeleton refreshes by polling (Phase 01A); Phase 08 replaces this with a live channel.
export function AutoRefresh({ intervalMs = 3000 }: { intervalMs?: number }) {
  const router = useRouter();

  useEffect(() => {
    const timer = window.setInterval(() => router.refresh(), intervalMs);
    return () => window.clearInterval(timer);
  }, [router, intervalMs]);

  return <p className="text-xs text-text-muted">Refreshes every {Math.round(intervalMs / 1000)} s</p>;
}
