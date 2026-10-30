import { CircleCheck, CircleX, TriangleAlert } from "lucide-react";

import { StatusBadge, type StatusTone } from "@/design-system/domain/status-badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import { formatDateTime } from "@/features/incidents/presentation";
import type { SourceState } from "@/lib/api/contracts";

const health: Record<SourceState["health"], { tone: StatusTone; icon: typeof CircleCheck; label: string }> = {
  Healthy: { tone: "stable", icon: CircleCheck, label: "Healthy" },
  Degraded: { tone: "warning", icon: TriangleAlert, label: "Degraded" },
  Unavailable: { tone: "critical", icon: CircleX, label: "Unavailable" },
};

// Ingestion's sources at a glance (Phase 02 operational view): which are healthy, how far each one's data is complete,
// how far that trailed its last pull, and for a failing source how often it failed, when it is tried next and why.
export function SourceHealth({ sources }: { sources: SourceState[] }) {
  const ordered = [...sources].sort((left, right) => severity(right) - severity(left) || left.sourceId.localeCompare(right.sourceId));
  const counts = (Object.keys(health) as SourceState["health"][]).map((name) => `${sources.filter((source) => source.health === name).length} ${name.toLowerCase()}`);
  return (
    <div className="flex flex-col gap-3">
      <p className="text-sm text-text-muted">{counts.join(", ")}.</p>
      <Table aria-label="Source health">
        <TableHeader>
          <TableRow>
            <TableHead>Source</TableHead>
            <TableHead>Health</TableHead>
            <TableHead>Complete through</TableHead>
            <TableHead>Failures</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ordered.map((source) => {
            const shown = health[source.health];
            return (
              <TableRow key={source.sourceId}>
                <TableCell className="font-mono text-xs">{source.sourceId}</TableCell>
                <TableCell>
                  <StatusBadge tone={shown.tone} icon={shown.icon}>
                    {shown.label}
                  </StatusBadge>
                </TableCell>
                <TableCell className="text-xs">
                  {formatDateTime(source.watermark)}
                  <span className="block text-text-muted">{trailing(source)} behind its last pull at {formatDateTime(source.lastPulledAt)}</span>
                </TableCell>
                <TableCell className="text-xs whitespace-normal">
                  {source.consecutiveFailures === 0 ? (
                    <span className="text-text-muted">None</span>
                  ) : (
                    <>
                      {source.consecutiveFailures} in a row{source.nextAttemptAt ? `, next try ${formatDateTime(source.nextAttemptAt)}` : ""}
                      {source.lastError ? <span className="block text-text-muted">{source.lastError}</span> : null}
                    </>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}

function severity(source: SourceState): number {
  return source.health === "Unavailable" ? 2 : source.health === "Degraded" ? 1 : 0;
}

function trailing(source: SourceState): string {
  const seconds = Math.max(0, Math.round((Date.parse(source.lastPulledAt) - Date.parse(source.watermark)) / 1000));
  return seconds < 120 ? `${seconds} s` : `${Math.round(seconds / 60)} min`;
}
