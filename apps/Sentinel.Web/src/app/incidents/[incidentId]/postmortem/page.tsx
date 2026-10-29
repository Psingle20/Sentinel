import Link from "next/link";
import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { FactList } from "@/design-system/patterns/fact-list";
import { PageHeader } from "@/design-system/patterns/page-header";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { NarrativeEditor } from "@/features/incidents/narrative-editor";
import { formatDateTime } from "@/features/incidents/presentation";
import { ReplayPanel } from "@/features/incidents/replay-panel";
import { sourcedFacts } from "@/features/incidents/view-models";
import type { PostmortemFact } from "@/lib/api/contracts";
import { getPostmortem } from "@/lib/api/server";

export default function PostmortemPage({ params }: PageProps<"/incidents/[incidentId]/postmortem">) {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-sm text-text-muted hover:text-text-primary focus-visible:text-text-primary">
          Incidents
        </Link>
      }
    >
      <Suspense fallback={<DataState kind="loading" title="Loading the postmortem" />}>
        <Postmortem params={params} />
      </Suspense>
    </AppShell>
  );
}

const sections: { key: "timeline" | "evidence" | "decision" | "actions" | "recovery" | "openQuestions"; title: string; description: string }[] = [
  { key: "decision", title: "Decision", description: "What Sentinel concluded and why each explanation won or lost." },
  { key: "evidence", title: "Evidence", description: "The accepted evidence, with the observations it cites." },
  { key: "actions", title: "Actions", description: "Plans, policy verdicts, approvals and executions." },
  { key: "recovery", title: "Recovery", description: "The measured recovery signals." },
  { key: "openQuestions", title: "Open questions", description: "What the investigation could not determine, and dissent that was kept." },
  { key: "timeline", title: "Timeline", description: "Every event of the incident stream." },
];

async function Postmortem({ params }: { params: Promise<{ incidentId: string }> }) {
  const { incidentId } = await params;
  let postmortem;
  try {
    postmortem = await getPostmortem(incidentId);
  } catch {
    return <DataState kind="error" title="The postmortem is unavailable" description="Sentinel.Api cannot be reached." />;
  }

  if (!postmortem) {
    return <DataState kind="empty" title="Incident not found" description="It may belong to another tenant or environment." />;
  }

  const facts = (list: PostmortemFact[] | undefined) => sourcedFacts(list, incidentId);
  return (
    <>
      <PageHeader
        title={`Postmortem: ${postmortem.title}`}
        description={`Generated ${formatDateTime(postmortem.generatedAt)} from stream version ${postmortem.streamVersion}. Every fact links to the history it comes from.`}
      />
      <div className="grid gap-6 lg:grid-cols-3">
        <div className="flex flex-col gap-6 lg:col-span-2">
          <Card aria-labelledby="summary-title">
            <CardHeader>
              <CardTitle id="summary-title">Summary</CardTitle>
            </CardHeader>
            <CardContent>
              <FactList facts={facts([postmortem.summary])} label="Summary" />
            </CardContent>
          </Card>
          {sections.map((section) => {
            const list = facts(postmortem[section.key]);
            return (
              <Card key={section.key} aria-labelledby={`${section.key}-title`}>
                <CardHeader>
                  <CardTitle id={`${section.key}-title`}>{section.title}</CardTitle>
                  <CardDescription>{section.description}</CardDescription>
                </CardHeader>
                <CardContent>{list.length === 0 ? <DataState kind="empty" title="Nothing recorded" /> : <FactList facts={list} label={section.title} />}</CardContent>
              </Card>
            );
          })}
        </div>
        <div className="flex flex-col gap-6">
          <Card aria-labelledby="narrative-title">
            <CardHeader>
              <CardTitle id="narrative-title">Narrative</CardTitle>
              <CardDescription>
                {postmortem.narrative
                  ? `Last edited by ${postmortem.narrative.editedBy} at ${formatDateTime(postmortem.narrative.editedAt)}.`
                  : "Not written yet."}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <NarrativeEditor incidentId={incidentId} markdown={postmortem.narrative?.markdown ?? ""} version={postmortem.narrative?.version ?? 0} />
            </CardContent>
          </Card>
          <Card aria-labelledby="replay-title">
            <CardHeader>
              <CardTitle id="replay-title">Replay</CardTitle>
              <CardDescription>Check that the stored history reproduces this incident exactly.</CardDescription>
            </CardHeader>
            <CardContent>
              <ReplayPanel incidentId={incidentId} />
            </CardContent>
          </Card>
          <Card aria-labelledby="versions-title">
            <CardHeader>
              <CardTitle id="versions-title">Versions</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="flex flex-col gap-2 text-sm">
                <div>
                  <dt className="text-xs text-text-muted">Decision algorithm</dt>
                  <dd className="text-text-primary">{postmortem.versions.decisionAlgorithm ?? "none"}</dd>
                </div>
                <div>
                  <dt className="text-xs text-text-muted">Models and prompts</dt>
                  <dd className="text-text-primary">
                    {(postmortem.versions.models ?? []).map((model) => `${model.provider}/${model.model} (${model.promptVersion})`).join(", ") || "none"}
                  </dd>
                </div>
                <div>
                  <dt className="text-xs text-text-muted">Policy</dt>
                  <dd className="text-text-primary">{postmortem.versions.policyVersion ?? "no plan"}</dd>
                </div>
              </dl>
            </CardContent>
          </Card>
        </div>
      </div>
    </>
  );
}
