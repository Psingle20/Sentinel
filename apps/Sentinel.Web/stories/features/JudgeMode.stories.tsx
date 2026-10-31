import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { JudgeView } from "@/features/judge/judge-view";
import type { Stage, Story as JudgeStory } from "@/features/judge/stages";

// Phase E04: Judge mode's every stage status and every ending, from hand-built stories rather than fetched records.
const incidentId = "0192f0c4-0000-7000-8000-000000000001";
const at = "2026-10-28T09:04:00+00:00";

function stage(id: Stage["id"], title: string, status: Stage["status"], summary: string): Stage {
  return {
    id,
    title,
    question: `What does the ${title.toLowerCase()} stage show?`,
    status,
    summary,
    at,
    facts: [{ text: `${title} record`, at: "09:04:00 UTC", sources: [{ kind: "event", ref: "4", href: `/incidents/${incidentId}#timeline-title` }] }],
    anchor: "#timeline-title",
  };
}

const opening = [
  stage("context", "Context", "complete", "checkout-service slo_burn_rate at 14.8. Critical severity, affecting checkout-service."),
  stage("detection", "Detection", "complete", "Sentinel opened the incident from 1 trigger observation, with 1 deployment in its window: checkout-service v2.4.1."),
  stage("investigation", "Investigation", "complete", "6 of 6 investigators reported."),
];

function story(stages: Stage[], outcome: JudgeStory["outcome"], executionsAvailable = true): JudgeStory {
  return { incidentId, stages, outcome, executionsAvailable, providers: ["sentinel-test-double"] };
}

const meta = {
  title: "Features/Judge mode",
  component: JudgeView,
  args: {
    mode: "captured",
    environment: "acme/reference",
    asOf: at,
    story: story(
      [
        ...opening,
        stage("decision", "Decision", "complete", "Sentinel diagnosed the v2.4.1 deployment at 72% confidence."),
        stage("governance", "Governance", "complete", "Plan v1: roll back checkout-service. responder@example.test approved it."),
        stage("action", "Action", "complete", "Action verified at the target."),
        stage("recovery", "Recovery", "complete", "Independent telemetry proved recovery: 2 of 2 criteria met."),
      ],
      { incident: "resolved", action: "Action verified at the target", recovery: "recovered", next: "Nothing: telemetry proved recovery." },
    ),
  },
} satisfies Meta<typeof JudgeView>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ProvenRecovery: Story = {};

export const LiveAwaitingApproval: Story = {
  args: {
    mode: "live",
    story: story(
      [...opening, stage("decision", "Decision", "complete", "Sentinel diagnosed the v2.4.1 deployment."), stage("governance", "Governance", "active", "Plan v1 waits for a person to approve it.")],
      { incident: "awaitingApproval", action: "Plan v1 awaitingApproval", next: "An approver reviews plan v1 in the workspace." },
    ),
  },
};

export const Abstained: Story = {
  args: {
    story: story(
      [...opening, stage("decision", "Decision", "inconclusive", "Sentinel abstained: two explanations stood too close to choose between, so it escalated rather than guess.")],
      { incident: "escalated", action: "No action proposed", next: "Responders take over, starting from what the decision could not determine." },
    ),
  },
};

export const PolicyDenied: Story = {
  args: {
    story: story(
      [...opening, stage("decision", "Decision", "complete", "Sentinel diagnosed the v2.4.1 deployment."), stage("governance", "Governance", "failed", "Policy denied it, so nothing runs.")],
      { incident: "escalated", action: "Plan v1 denied", next: "Responders take over, starting from what the decision could not determine." },
    ),
  },
};

export const ActionFailed: Story = {
  args: {
    story: story(
      [...opening, stage("governance", "Governance", "complete", "responder@example.test approved it."), stage("action", "Action", "failed", "Failed: the deploy controller refused the rollback.")],
      { incident: "escalated", action: "Failed", next: "Responders take over, starting from what the decision could not determine." },
    ),
  },
};

export const RecoveryInconclusive: Story = {
  args: {
    story: story(
      [...opening, stage("action", "Action", "complete", "Action verified at the target."), stage("recovery", "Recovery", "inconclusive", "Recovery inconclusive: 0 of 2 criteria met. The metrics provider covered half the window.")],
      { incident: "escalated", action: "Action verified at the target", recovery: "inconclusive", next: "Responders take over, starting from what the decision could not determine." },
      false,
    ),
  },
};
