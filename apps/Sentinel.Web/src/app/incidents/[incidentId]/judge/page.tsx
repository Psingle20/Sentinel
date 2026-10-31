import Link from "next/link";
import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { LiveUpdates } from "@/features/incidents/live-updates";
import { JudgeView } from "@/features/judge/judge-view";
import { deriveStory, unplayable, type StoryMode } from "@/features/judge/stages";
import type { AgentExecution, IncidentDetail } from "@/lib/api/contracts";
import { getAgentExecutions, getIncident, getRunRecord } from "@/lib/api/server";

// Phase E04, Judge mode: the incident's story, live from the incident queries or captured from its run record.
export default function JudgePage({ params, searchParams }: PageProps<"/incidents/[incidentId]/judge">) {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-body text-text-muted hover:text-text-primary focus-visible:text-text-primary">
          Incidents
        </Link>
      }
    >
      <Suspense fallback={<DataState kind="loading" title="Loading the incident's story" />}>
        <Judge params={params} searchParams={searchParams} />
      </Suspense>
    </AppShell>
  );
}

type Loaded =
  | { kind: "story"; incident: IncidentDetail; asOf: string }
  | { kind: "missing"; title: string }
  | { kind: "refused"; reason: string }
  | { kind: "unavailable" };

// What the page shows, from the run record for a captured story or the incident queries for a live one.
async function load(incidentId: string, mode: StoryMode): Promise<Loaded> {
  try {
    if (mode === "captured") {
      const record = await getRunRecord(incidentId);
      if (!record) {
        return { kind: "missing", title: "No run record for this incident" };
      }

      const refusal = unplayable(record);
      return refusal ? { kind: "refused", reason: `${refusal} Its versions: ${JSON.stringify(record.versions ?? {})}.` } : { kind: "story", incident: record.incident, asOf: record.exportedAt };
    }

    const incident = await getIncident(incidentId);
    return incident ? { kind: "story", incident, asOf: incident.summary.updatedAt } : { kind: "missing", title: "Incident not found" };
  } catch {
    return { kind: "unavailable" };
  }
}

async function Judge({ params, searchParams }: { params: Promise<{ incidentId: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { incidentId } = await params;
  const mode: StoryMode = (await searchParams).source === "captured" ? "captured" : "live";
  const loaded = await load(incidentId, mode);
  if (loaded.kind === "unavailable") {
    return <DataState kind="error" title="This incident's story is unavailable" description="Sentinel.Gateway cannot be reached." />;
  }

  if (loaded.kind === "refused") {
    return <DataState kind="error" title="This run record cannot be played" description={loaded.reason} />;
  }

  if (loaded.kind === "missing") {
    return <DataState kind="empty" title={loaded.title} description="It may belong to another tenant or environment." />;
  }

  const { incident, asOf } = loaded;
  // Model detail degrades on its own: unavailable executions are marked so, never inferred from the diagnosis.
  const executions = await getAgentExecutions(incidentId).catch((): AgentExecution[] | null => null);
  const story = deriveStory(incident, executions);
  const here = `/incidents/${encodeURIComponent(incidentId)}/judge`;
  return (
    <>
      <PageHeader
        title={incident.summary.title}
        actions={
          mode === "live" ? (
            <div className="flex items-center gap-3">
              <Link href={`${here}?source=captured`} className="text-body text-text-muted underline-offset-4 hover:text-text-primary hover:underline focus-visible:text-text-primary">
                Play the captured run
              </Link>
              <LiveUpdates incidentId={incident.summary.incidentId} />
            </div>
          ) : (
            <Link href={here} className="text-body text-text-muted underline-offset-4 hover:text-text-primary hover:underline focus-visible:text-text-primary">
              Follow it live
            </Link>
          )
        }
      />
      <JudgeView story={story} mode={mode} environment={`${incident.summary.tenantId}/${incident.summary.environmentId}`} asOf={asOf} />
    </>
  );
}
