import { Suspense } from "react";

import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { areaDescriptions, areaTitles, isConfigurationArea } from "@/features/admin/areas";
import { ControlPlaneUnavailable } from "@/features/admin/control-plane-unavailable";
import { ConfigurationEditor } from "@/features/admin/controls";
import { TrackView } from "@/features/analytics/track-view";
import { formatDateTime } from "@/features/incidents/presentation";
import { type ConfigurationArea, configurationAreas, type DocumentState, seedAuthor } from "@/lib/api/contracts";
import { getConfiguration, getConfigurationHistory } from "@/lib/api/server";

// The areas are fixed, so every one is known when the page is built.
export function generateStaticParams() {
  return configurationAreas.map((area) => ({ area }));
}

export default function ConfigurationPage({ params }: PageProps<"/admin/configuration/[area]">) {
  return (
    <Suspense fallback={<DataState kind="loading" title="Loading the configuration" />}>
      <Configuration params={params} />
    </Suspense>
  );
}

async function Configuration({ params }: { params: Promise<{ area: string }> }) {
  const { area } = await params;
  if (!isConfigurationArea(area)) {
    return <DataState kind="empty" title="No such configuration area" description="Choose one of the areas above." />;
  }

  return (
    <>
      <PageHeader title={areaTitles[area]} description={areaDescriptions[area]} />
      <TrackView name="admin.viewed" properties={{ area }} />
      <Suspense fallback={<DataState kind="loading" title={`Loading ${areaTitles[area].toLowerCase()}`} />}>
        <Area area={area} />
      </Suspense>
    </>
  );
}

const owners: Record<ConfigurationArea, string> = {
  runtime: "Sentinel.Gateway or Runtime",
  models: "Sentinel.Gateway or AgentWorker",
  sources: "Sentinel.Gateway or Ingestion",
  api: "Sentinel.Gateway",
};

async function Area({ area }: { area: ConfigurationArea }) {
  let data;
  try {
    data = await Promise.all([getConfiguration(area), getConfigurationHistory(area)]);
  } catch (error) {
    return <ControlPlaneUnavailable error={error} owners={owners[area]} />;
  }

  const [state, history] = data;
  return (
    <div className="grid gap-6 lg:grid-cols-3">
      <Card aria-labelledby="document-title" className="lg:col-span-2">
        <CardHeader>
          <CardTitle id="document-title">Version {state.version.version}</CardTitle>
          <CardDescription>{provenance(state)}</CardDescription>
        </CardHeader>
        <CardContent>
          <ConfigurationEditor area={area} document={JSON.stringify(state.document, null, 2)} version={state.version.version} />
        </CardContent>
      </Card>
      <div className="flex flex-col gap-6">
        <Card aria-labelledby="summary-title">
          <CardHeader>
            <CardTitle id="summary-title">What it holds</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid grid-cols-2 gap-x-3 gap-y-1.5 text-sm">
              {Object.entries(state.document)
                .filter(([key]) => key !== "version")
                .map(([key, value]) => (
                  <div key={key} className="contents">
                    <dt className="font-mono text-xs text-text-muted">{key}</dt>
                    <dd className="text-text-primary">{summarize(value)}</dd>
                  </div>
                ))}
            </dl>
          </CardContent>
        </Card>
        <Card aria-labelledby="versions-title">
          <CardHeader>
            <CardTitle id="versions-title">Versions</CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="flex flex-col gap-2 text-sm" aria-label="Configuration versions">
              {history.map((entry) => (
                <li key={entry.version} className="flex flex-col gap-0.5">
                  <span className="text-text-primary">{entry.version}</span>
                  <span className="text-xs text-text-muted">
                    {entry.author === seedAuthor
                      ? "Deployment defaults"
                      : `${entry.author}, ${formatDateTime(entry.publishedAt)}${entry.basedOn ? `, based on ${entry.basedOn}` : ""}`}
                  </span>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function provenance(state: DocumentState): string {
  if (!state.published) {
    return "The deployment's defaults: nothing has been published from the control plane yet.";
  }

  const basedOn = state.version.basedOn ? `, based on ${state.version.basedOn}` : "";
  return `Published by ${state.version.author} at ${formatDateTime(state.version.publishedAt)}${basedOn}.`;
}

function summarize(value: unknown): string {
  if (Array.isArray(value)) {
    return value.length === 1 ? "1 entry" : `${value.length} entries`;
  }

  if (value !== null && typeof value === "object") {
    const count = Object.keys(value).length;
    return count === 1 ? "1 setting" : `${count} settings`;
  }

  return String(value);
}
