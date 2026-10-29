import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { AgentActivity, type AgentRun } from "@/design-system/patterns/agent-activity";

const runs: AgentRun[] = [
  { id: "a1", investigator: "model", role: "metrics", shadow: false, model: "sentinel-test-double/deterministic-analyst", promptVersion: "investigator-v3/metrics", result: "completed", toolCalls: 4, failedToolCalls: 0, tokens: 4986, durationSeconds: 3.2 },
  { id: "a2", investigator: "model", role: "logs", shadow: false, model: "sentinel-test-double/deterministic-analyst", promptVersion: "investigator-v3/logs", result: "abstained", toolCalls: 2, failedToolCalls: 1, tokens: 2310, durationSeconds: 31.4, note: "Logs were unavailable from loki." },
  { id: "a3", investigator: "adversarial-verifier", role: "verifier", shadow: false, model: "sentinel/adversarial-verifier", promptVersion: "falsifiers-v1", result: "completed", toolCalls: 4, failedToolCalls: 0, tokens: 0, durationSeconds: 1.1 },
  { id: "a4", investigator: "deploy-blame", shadow: true, model: "sentinel/deploy-blame", promptVersion: "deploy-blame-v1", result: "completed", toolCalls: 1, failedToolCalls: 0, tokens: 0, durationSeconds: 0.2 },
];

const meta = {
  title: "Patterns/Agent activity",
  component: AgentActivity,
  args: { runs },
} satisfies Meta<typeof AgentActivity>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Investigation: Story = {};

export const FailedRun: Story = {
  args: { runs: [{ ...runs[0], result: "failed", failedToolCalls: 4, note: "The observation query failed: Sentinel.Ingestion returned 503." }] },
};
