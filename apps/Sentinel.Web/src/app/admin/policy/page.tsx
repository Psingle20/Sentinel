import { Suspense } from "react";

import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { ControlPlaneUnavailable } from "@/features/admin/control-plane-unavailable";
import { PolicyEditor } from "@/features/admin/controls";
import { TrackView } from "@/features/analytics/track-view";
import { formatDateTime } from "@/features/incidents/presentation";
import { getPolicy, getPolicyHistory } from "@/lib/api/server";

export default function PolicyPage() {
  return (
    <>
      <PageHeader
        title="Remediation policy"
        description="Which actions each environment permits, at what risk, confidence and blast radius, and whether they need approval. Owned and validated by Sentinel.Executor."
      />
      <TrackView name="admin.viewed" properties={{ area: "policy" }} />
      <Suspense fallback={<DataState kind="loading" title="Loading the policy" />}>
        <Policy />
      </Suspense>
    </>
  );
}

async function Policy() {
  let data;
  try {
    data = await Promise.all([getPolicy(), getPolicyHistory()]);
  } catch (error) {
    return <ControlPlaneUnavailable error={error} owners="Sentinel.Gateway or Executor" />;
  }

  const [policy, history] = data;
  const version = String(policy.version ?? "");
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <Card aria-labelledby="policy-title" className="lg:col-span-2">
        <CardHeader>
          <CardTitle id="policy-title">Remediation policy {version}</CardTitle>
          <CardDescription>The current policy as code. Publishing a new version takes effect for the next evaluation.</CardDescription>
        </CardHeader>
        <CardContent>
          <PolicyEditor document={JSON.stringify(policy, null, 2)} version={version} />
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
    </div>
  );
}
