import { OctagonX, ShieldCheck } from "lucide-react";
import Link from "next/link";
import { Suspense } from "react";

import { StatusBadge } from "@/design-system/domain/status-badge";
import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/design-system/primitives/table";
import { EmergencyStop, PolicyEditor } from "@/features/admin/controls";
import { formatDateTime } from "@/features/incidents/presentation";
import { ApiError, getAudit, getControls, getPolicy, getPolicyHistory, getSources } from "@/lib/api/server";

export default function AdminPage() {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-sm text-text-muted hover:text-text-primary focus-visible:text-text-primary">
          Incidents
        </Link>
      }
    >
      <PageHeader title="Control plane" description="Policy, operator controls and provider sources. Every change is audited." />
      <Suspense fallback={<DataState kind="loading" title="Loading the control plane" />}>
        <ControlPlane />
      </Suspense>
    </AppShell>
  );
}

async function ControlPlane() {
  let data;
  try {
    data = await Promise.all([getPolicy(), getPolicyHistory(), getControls(), getSources(), getAudit()]);
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) {
      return <DataState kind="permission-denied" title="The control plane is for operators" description="Ask an administrator for the operator role." />;
    }
    return <DataState kind="error" title="The control plane is unavailable" description="Sentinel.Api, Executor or Ingestion did not answer." />;
  }

  const [policy, history, controls, sources, audit] = data;
  const version = String(policy.version ?? "");
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <div className="flex flex-col gap-6 lg:col-span-2">
        <Card aria-labelledby="policy-title">
          <CardHeader>
            <CardTitle id="policy-title">Remediation policy {version}</CardTitle>
            <CardDescription>The current policy as code. Publishing a new version takes effect for the next evaluation.</CardDescription>
          </CardHeader>
          <CardContent>
            <PolicyEditor document={JSON.stringify(policy, null, 2)} version={version} />
          </CardContent>
        </Card>
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
      </div>
      <div className="flex flex-col gap-6">
        <Card aria-labelledby="controls-title">
          <CardHeader>
            <CardTitle id="controls-title">Operator controls</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <StatusBadge tone={controls.emergencyStop ? "critical" : "stable"} icon={controls.emergencyStop ? OctagonX : ShieldCheck}>
              {controls.emergencyStop ? "Emergency stop engaged" : "Actions follow policy"}
            </StatusBadge>
            {controls.disabledActions.length > 0 ? <p className="text-sm text-text-muted">Disabled actions: {controls.disabledActions.join(", ")}</p> : null}
            <EmergencyStop engaged={controls.emergencyStop} />
          </CardContent>
        </Card>
        <Card aria-labelledby="history-title">
          <CardHeader>
            <CardTitle id="history-title">Policy versions</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="flex flex-col gap-2 text-sm" aria-label="Policy versions">
              {history.map((entry) => (
                <li key={`${entry.version}-${entry.source}`} className="flex flex-col gap-0.5">
                  <span className="text-text-primary">{entry.version}</span>
                  <span className="text-xs text-text-muted">
                    {entry.source === "file" ? "Repository seed" : `${entry.author}, ${formatDateTime(entry.publishedAt)}`}
                  </span>
                </li>
              ))}
            </ul>
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
      </div>
    </div>
  );
}
