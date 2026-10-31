import Link from "next/link";
import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { TrackView } from "@/features/analytics/track-view";
import { LiveUpdates } from "@/features/incidents/live-updates";
import { IncidentDetailView, IncidentHeaderBadges } from "@/features/incidents/incident-detail";
import type { AgentExecution, SimilarIncident } from "@/lib/api/contracts";
import { getAgentExecutions, getIncident, getSimilarIncidents } from "@/lib/api/server";

export default function IncidentPage({ params }: PageProps<"/incidents/[incidentId]">) {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-body text-text-muted hover:text-text-primary focus-visible:text-text-primary">
          Incidents
        </Link>
      }
    >
      <Suspense fallback={<DataState kind="loading" title="Loading incident" />}>
        <Incident params={params} />
      </Suspense>
    </AppShell>
  );
}

async function Incident({ params }: { params: Promise<{ incidentId: string }> }) {
  const { incidentId } = await params;
  let incident;
  try {
    incident = await getIncident(incidentId);
  } catch {
    return <DataState kind="error" title="This incident is unavailable" description="Sentinel.Gateway cannot be reached. The page updates once it is back." />;
  }

  if (!incident) {
    return <DataState kind="empty" title="Incident not found" description="It may belong to another tenant or environment." />;
  }

  // The side panels degrade on their own: an unavailable AgentWorker or memory never hides the incident.
  const [executions, similar] = await Promise.all([
    getAgentExecutions(incidentId).catch((): AgentExecution[] | null => null),
    getSimilarIncidents(incidentId).catch((): SimilarIncident[] | null => null),
  ]);

  return (
    <>
      <PageHeader title={incident.summary.title} actions={<LiveUpdates incidentId={incident.summary.incidentId} />} />
      <TrackView name="incident.viewed" properties={{ status: incident.summary.status, severity: incident.summary.severity }} />
      <IncidentHeaderBadges incident={incident} />
      <IncidentDetailView incident={incident} executions={executions} similar={similar} />
    </>
  );
}
