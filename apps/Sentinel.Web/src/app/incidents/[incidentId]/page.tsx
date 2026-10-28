import Link from "next/link";
import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { AutoRefresh } from "@/features/incidents/auto-refresh";
import { IncidentDetailView, IncidentHeaderBadges } from "@/features/incidents/incident-detail";
import { getIncident } from "@/lib/api/server";

export default function IncidentPage({ params }: PageProps<"/incidents/[incidentId]">) {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-sm text-text-muted hover:text-text-primary focus-visible:text-text-primary">
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
    return <DataState kind="error" title="This incident is unavailable" description="Sentinel.Api cannot be reached. The page refreshes automatically." />;
  }

  if (!incident) {
    return <DataState kind="empty" title="Incident not found" description="It may belong to another tenant or environment." />;
  }

  return (
    <>
      <PageHeader title={incident.summary.title} actions={<AutoRefresh />} />
      <IncidentHeaderBadges incident={incident} />
      <IncidentDetailView incident={incident} />
    </>
  );
}
