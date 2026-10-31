"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Archive, CircleCheck, CircleHelp, CircleX, Pause, Play, Radio, type LucideIcon } from "lucide-react";

import { IncidentStatusBadge } from "@/design-system/domain/incident-status-badge";
import { RecoveryState } from "@/design-system/domain/recovery-state";
import { StatusBadge, type StatusTone } from "@/design-system/domain/status-badge";
import { FactList } from "@/design-system/patterns/fact-list";
import { Button } from "@/design-system/primitives/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/design-system/primitives/card";
import { formatDateTime, incidentStatus, recoveryStatus } from "@/features/incidents/presentation";

import type { Stage, StageId, StageStatus, Story, StoryMode } from "./stages";

// Captured playback steps through the recorded stages at this pace; the timestamps shown stay the recorded ones.
const playbackStepMs = 3_000;

const stageStatuses: Record<StageStatus, { label: string; tone: StatusTone; icon: LucideIcon }> = {
  active: { label: "In progress", tone: "executing", icon: Radio },
  complete: { label: "Complete", tone: "stable", icon: CircleCheck },
  failed: { label: "Failed", tone: "failed", icon: CircleX },
  inconclusive: { label: "Inconclusive", tone: "warning", icon: CircleHelp },
};

type Props = {
  story: Story;
  mode: StoryMode;
  // The environment the incident belongs to, as tenant/environment.
  environment: string;
  // When the run record was exported (captured) or the incident last changed (live).
  asOf: string;
};

// Phase E04: one incident as a guided story in five regions: the truth bar, the stage rail, the proof canvas, the
// evidence drawer and the outcome strip. It is read-only; approval stays in the workspace's own dialog.
export function JudgeView({ story, mode, environment, asOf }: Props) {
  const { stages } = story;
  // Live mode follows the newest stage until the viewer picks one; a captured story starts at the beginning.
  const [picked, setPicked] = useState<StageId | null>(mode === "captured" ? (stages[0]?.id ?? null) : null);
  const [playing, setPlaying] = useState(false);
  const current = stages.find((stage) => stage.id === picked) ?? stages.at(-1);
  const index = current ? stages.indexOf(current) : -1;

  // Each step waits on a timer, and playback stops by itself on the last stage.
  useEffect(() => {
    if (!playing || index >= stages.length - 1) {
      return;
    }

    const timer = window.setTimeout(() => {
      setPicked(stages[index + 1].id);
      if (index + 1 >= stages.length - 1) {
        setPlaying(false);
      }
    }, playbackStepMs);
    return () => window.clearTimeout(timer);
  }, [playing, index, stages]);

  if (!current) {
    return null;
  }

  const workspace = `/incidents/${encodeURIComponent(story.incidentId)}`;
  return (
    <div className="flex flex-col gap-4">
      <TruthBar story={story} mode={mode} environment={environment} asOf={asOf} playing={playing} />
      <div className="grid gap-4 lg:grid-cols-4">
        <nav aria-label="Stages" className="lg:col-span-1">
          <ol className="flex flex-col gap-1">
            {stages.map((stage) => {
              const status = stageStatuses[stage.status];
              const selected = stage.id === current.id;
              return (
                <li key={stage.id}>
                  <button
                    type="button"
                    aria-current={selected ? "step" : undefined}
                    onClick={() => {
                      setPlaying(false);
                      setPicked(stage.id);
                    }}
                    className={
                      selected
                        ? "flex w-full items-center justify-between gap-2 rounded-md border border-action-executing px-2 py-1.5 text-left text-body font-medium text-text-primary"
                        : "flex w-full items-center justify-between gap-2 rounded-md border border-transparent px-2 py-1.5 text-left text-body text-text-muted hover:text-text-primary focus-visible:text-text-primary"
                    }
                  >
                    <span>{stage.title}</span>
                    <StatusBadge tone={status.tone} icon={status.icon}>
                      {status.label}
                    </StatusBadge>
                  </button>
                </li>
              );
            })}
          </ol>
          <div className="mt-3 flex flex-wrap gap-2">
            <Button variant="outline" size="sm" disabled={index <= 0} onClick={() => { setPlaying(false); setPicked(stages[index - 1].id); }}>
              Previous
            </Button>
            <Button variant="outline" size="sm" disabled={index >= stages.length - 1} onClick={() => { setPlaying(false); setPicked(stages[index + 1].id); }}>
              Next
            </Button>
            {mode === "captured" ? (
              <Button variant="outline" size="sm" onClick={() => setPlaying(!playing)} disabled={!playing && index >= stages.length - 1}>
                {playing ? <Pause aria-hidden="true" /> : <Play aria-hidden="true" />}
                {playing ? "Pause" : "Play"}
              </Button>
            ) : picked !== null ? (
              <Button variant="outline" size="sm" onClick={() => setPicked(null)}>
                Follow newest
              </Button>
            ) : null}
          </div>
        </nav>
        <div className="flex flex-col gap-4 lg:col-span-3">
          <ProofCanvas stage={current} />
          <Card role="region" aria-label="Sources">
            <CardHeader>
              <CardTitle>Sources</CardTitle>
              <CardDescription>The recorded facts this stage rests on. Each names the record it comes from.</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-3">
              {current.facts.length ? <FactList facts={current.facts} label={`${current.title} sources`} /> : <p className="text-body text-text-muted">No records yet.</p>}
              <Link href={`${workspace}${current.anchor}`} className="text-body text-text-primary underline-offset-4 hover:underline focus-visible:underline">
                Open in the incident workspace
              </Link>
            </CardContent>
          </Card>
        </div>
      </div>
      <OutcomeStrip story={story} />
    </div>
  );
}

function TruthBar({ story, mode, environment, asOf, playing }: Props & { playing: boolean }) {
  const providers = story.providers.map((provider) => (provider === "sentinel-test-double" ? "deterministic test double" : provider));
  return (
    <section aria-label="Truth bar" className="flex flex-wrap items-center gap-x-4 gap-y-2 rounded-lg border border-border-subtle px-3 py-2 text-body">
      {mode === "live" ? (
        <StatusBadge tone="stable" icon={Radio}>
          Live
        </StatusBadge>
      ) : (
        <StatusBadge tone="neutral" icon={Archive}>
          Captured
        </StatusBadge>
      )}
      <span className="text-text-muted">{environment}</span>
      <span className="font-mono text-caption text-text-muted">{story.incidentId}</span>
      <span className="text-text-muted">{mode === "live" ? `Updated ${formatDateTime(asOf)}` : `Captured ${formatDateTime(asOf)}`}</span>
      <span className="text-text-muted">
        {story.executionsAvailable ? `Models: ${providers.length ? providers.join(", ") : "none ran"}` : "Agent-execution detail unavailable"}
      </span>
      {playing ? <span className="text-text-muted">Playing at accelerated speed; every time shown is the recorded one.</span> : null}
    </section>
  );
}

function ProofCanvas({ stage }: { stage: Stage }) {
  const status = stageStatuses[stage.status];
  return (
    <Card role="region" aria-labelledby="stage-title">
      <CardHeader>
        <CardTitle id="stage-title" className="flex flex-wrap items-center gap-2">
          {stage.title}
          <StatusBadge tone={status.tone} icon={status.icon}>
            {status.label}
          </StatusBadge>
        </CardTitle>
        <CardDescription>{stage.question}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <p className="text-body text-text-primary" aria-label="Summary">
          {stage.summary}
        </p>
        <p className="text-caption text-text-muted">
          {stage.at ? <>Recorded <time dateTime={stage.at}>{formatDateTime(stage.at)}</time>. </> : null}
          Summarized by Sentinel.Web from the sources below; the summary adds nothing they do not say.
        </p>
      </CardContent>
    </Card>
  );
}

function OutcomeStrip({ story }: { story: Story }) {
  const { outcome } = story;
  return (
    <section aria-label="Outcome" className="rounded-lg border border-border-subtle px-3 py-2">
      <dl className="grid gap-2 text-body sm:grid-cols-4">
        <div>
          <dt className="text-caption text-text-muted">Incident</dt>
          <dd>
            <IncidentStatusBadge status={incidentStatus(outcome.incident)} />
          </dd>
        </div>
        <div>
          <dt className="text-caption text-text-muted">Action</dt>
          <dd className="text-text-primary">{outcome.action}</dd>
        </div>
        <div>
          <dt className="text-caption text-text-muted">Recovery</dt>
          <dd className="text-text-primary">{outcome.recovery ? <RecoveryState status={recoveryStatus(outcome.recovery)} /> : "Not measured"}</dd>
        </div>
        <div>
          <dt className="text-caption text-text-muted">Next human step</dt>
          <dd className="text-text-primary">{outcome.next}</dd>
        </div>
      </dl>
    </section>
  );
}
