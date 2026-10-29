import { OctagonX, ShieldCheck } from "lucide-react";
import Link from "next/link";
import { Suspense } from "react";

import { StatusBadge } from "@/design-system/domain/status-badge";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import { ControlPlaneUnavailable } from "@/features/admin/control-plane-unavailable";
import { ActionOverrides, EmergencyStop } from "@/features/admin/controls";
import { TrackView } from "@/features/analytics/track-view";
import { formatDateTime } from "@/features/incidents/presentation";
import { type AnalyticsCount, remediationActionKinds } from "@/lib/api/contracts";
import { getAnalytics, getAudit, getControls, getPolicy, getSources } from "@/lib/api/server";

export default function AdminPage() {
  return (
    <>
      <PageHeader title="Control plane" description="Operator controls, provider sources and the audit history. Every change is audited." />
      <TrackView name="admin.viewed" properties={{ area: "overview" }} />
      <Suspense fallback={<DataState kind="loading" title="Loading the control plane" />}>
        <ControlPlane />
      </Suspense>
    </>
  );
}

async function ControlPlane() {
  let data;
  try {
    // Usage counts are a side panel: when they cannot be read, the controls still work.
    data = await Promise.all([getPolicy(), getControls(), getSources(), getAudit(), getAnalytics(30).catch(() => null)]);
  } catch (error) {
    return <ControlPlaneUnavailable error={error} owners="Sentinel.Gateway, Executor or Ingestion" />;
  }

  const [policy, controls, sources, audit, counts] = data;
  // A kind disabled at Executor stays listed even if this web app does not know it yet.
  const kinds = [...new Set([...remediationActionKinds, ...controls.disabledActions])];
  const usage = counts === null ? null : totals(counts);
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="flex flex-col gap-6 lg:col-span-2">
        <Card aria-labelledby="audit-title">
          <CardHeader>
            <CardTitle id="audit-title">Audit history</CardTitle>
            <CardDescription>Control-plane changes, newest first, including refused ones.</CardDescription>
          </CardHeader>
          <CardContent>
            {audit.length === 0 ? (
              <DataState kind="empty" title="No changes yet" />
            ) : (
              <Table aria-label="Audit history">
                <TableHeader>
                  <TableRow>
                    <TableHead>When</TableHead>
                    <TableHead>Who</TableHead>
                    <TableHead>What</TableHead>
                    <TableHead>Target</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {audit.map((entry) => (
                    <TableRow key={entry.sequence}>
                      <TableCell>{formatDateTime(entry.occurredAt)}</TableCell>
                      <TableCell>{entry.actor}</TableCell>
                      <TableCell>{entry.action}</TableCell>
                      <TableCell className="font-mono text-xs">{entry.target}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
        <Card aria-labelledby="sources-title">
          <CardHeader>
            <CardTitle id="sources-title">Provider sources</CardTitle>
            <CardDescription>Ingestion&apos;s sources with their health and watermarks.</CardDescription>
          </CardHeader>
          <CardContent>
            {sources.length === 0 ? (
              <DataState kind="empty" title="No source has been pulled yet" />
            ) : (
              <ul className="flex flex-col gap-2 text-xs" aria-label="Provider sources">
                {sources.map((source, index) => (
                  <li key={String(source.sourceId ?? index)} className="flex flex-col gap-0.5">
                    <span className="font-mono text-text-primary">{String(source.sourceId ?? "source")}</span>
                    <span className="text-text-muted">
                      {Object.entries(source)
                        .filter(([key]) => key !== "sourceId")
                        .map(([key, value]) => `${key}: ${typeof value === "object" ? JSON.stringify(value) : String(value)}`)
                        .join(" · ")}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>
        <Card aria-labelledby="usage-title">
          <CardHeader>
            <CardTitle id="usage-title">Product usage</CardTitle>
            <CardDescription>Approved analytics events over the last 30 days. Only daily counts are kept; nothing identifies a person, an incident or a tenant.</CardDescription>
          </CardHeader>
          <CardContent>
            {usage === null ? (
              <DataState kind="error" title="Usage counts are unavailable" description="Sentinel.Gateway did not answer." />
            ) : usage.length === 0 ? (
              <DataState kind="empty" title="Nothing counted yet" />
            ) : (
              <Table aria-label="Product usage">
                <TableHeader>
                  <TableRow>
                    <TableHead>Event</TableHead>
                    <TableHead>Values</TableHead>
                    <TableHead className="text-right">Count</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {usage.map((row) => (
                    <TableRow key={`${row.event}|${row.dimensions}`}>
                      <TableCell className="font-mono text-xs">{row.event}</TableCell>
                      <TableCell className="font-mono text-xs">{row.dimensions.replaceAll(";", ", ") || "None"}</TableCell>
                      <TableCell className="text-right tabular-nums">{row.count}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      </div>
      <div className="flex flex-col gap-6">
        <Card aria-labelledby="controls-title">
          <CardHeader>
            <CardTitle id="controls-title">Emergency stop</CardTitle>
            <CardDescription>Independent of every model and workflow path.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <StatusBadge tone={controls.emergencyStop ? "critical" : "stable"} icon={controls.emergencyStop ? OctagonX : ShieldCheck}>
              {controls.emergencyStop ? "Emergency stop engaged" : "Actions follow policy"}
            </StatusBadge>
            <EmergencyStop engaged={controls.emergencyStop} />
          </CardContent>
        </Card>
        <Card aria-labelledby="overrides-title">
          <CardHeader>
            <CardTitle id="overrides-title">Manual overrides</CardTitle>
            <CardDescription>Take one kind of action out of service, whatever the policy permits.</CardDescription>
          </CardHeader>
          <CardContent>
            <ActionOverrides kinds={kinds} disabled={controls.disabledActions} />
          </CardContent>
        </Card>
        <Card aria-labelledby="policy-title">
          <CardHeader>
            <CardTitle id="policy-title">Remediation policy {String(policy.version ?? "")}</CardTitle>
            <CardDescription>In force for the next evaluation.</CardDescription>
          </CardHeader>
          <CardContent>
            <Link href="/admin/policy" className="text-sm text-text-primary underline underline-offset-4">
              Review or publish the policy
            </Link>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

// One row per event and values over the whole period, most frequent first.
function totals(counts: AnalyticsCount[]): AnalyticsCount[] {
  const rows = new Map<string, AnalyticsCount>();
  for (const count of counts) {
    const key = `${count.event}|${count.dimensions}`;
    const row = rows.get(key);
    rows.set(key, row ? { ...row, count: row.count + count.count } : count);
  }

  return [...rows.values()].sort((left, right) => right.count - left.count || left.event.localeCompare(right.event));
}
