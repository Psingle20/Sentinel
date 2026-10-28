import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { AutoRefresh } from "@/features/incidents/auto-refresh";
import { IncidentTable } from "@/features/incidents/incident-table";
import type { IncidentSummary } from "@/lib/api/contracts";
import { ApiUnavailableError, listIncidents } from "@/lib/api/server";

export default function IncidentsPage() {
  return (
    <AppShell>
      <PageHeader
        title="Incidents"
        description="Correlated incidents across monitored services."
        actions={<AutoRefresh />}
      />
      <Suspense fallback={<DataState kind="loading" title="Loading incidents" />}>
        <Incidents />
      </Suspense>
    </AppShell>
  );
}

async function Incidents() {
  let incidents: IncidentSummary[];
  try {
    incidents = await listIncidents();
  } catch (error) {
    const unreachable = error instanceof ApiUnavailableError;
    return (
      <DataState
        kind="error"
        title="Incidents are unavailable"
        description={unreachable ? "Sentinel.Api cannot be reached. The list refreshes automatically." : "Sentinel.Api returned an error."}
      />
    );
  }

  if (incidents.length === 0) {
    return (
      <DataState
        kind="empty"
        title="No incidents yet"
        description="Incidents appear here when Sentinel correlates telemetry from a monitored service."
      />
    );
  }

  return <IncidentTable incidents={incidents} />;
}
