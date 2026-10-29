import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Button } from "@/design-system/primitives/button";
import { RemediationReview, type ReviewedPlan } from "@/design-system/patterns/remediation-review";

const rollback: ReviewedPlan = {
  version: 1,
  actions: ["Roll back checkout-service from v2.4.1 to v2.4.0"],
  risk: "medium",
  blastRadius: ["checkout-service"],
  preconditions: ["checkout-service serving-revision is v2.4.1"],
  expectedEffect: "checkout-service serves v2.4.0 again, and checkout-service's recovery signals return within 90 s.",
  expectedRecovery: ["checkout-service slo_burn_rate < 1"],
  observationWindow: "00:01:30",
  compensation: "None automatic: undoing the rollback would redeploy checkout-service v2.4.1, which is a human decision.",
  policy: { version: "policy-2026.10.2", decision: "requires approval", trace: ["rollback.reference-services: rollback on checkout-service in reference grants ApprovalRequired"] },
  planHash: "sha256:9f2c4e1d7a8b6c5e3f1a0b9c8d7e6f5a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e",
  approval: "required",
};

const meta = {
  title: "Patterns/Remediation review",
  component: RemediationReview,
  args: { plan: rollback },
} satisfies Meta<typeof RemediationReview>;

export default meta;

type Story = StoryObj<typeof meta>;

export const AwaitingApproval: Story = {
  args: {
    children: (
      <div className="flex gap-2">
        <Button>Approve plan</Button>
        <Button variant="outline">Reject plan</Button>
      </div>
    ),
  },
};

export const Approved: Story = { args: { plan: { ...rollback, approval: "approved", approvedBy: "responder@example.test" } } };

// A batch saturating a shared dependency: policy allows only a suggestion, which Sentinel never executes.
export const SuggestionOnly: Story = {
  args: {
    plan: {
      ...rollback,
      actions: ["Pause the reporting-worker batch"],
      risk: "low",
      blastRadius: ["reporting-worker"],
      preconditions: ["reporting-worker batch-active is active"],
      expectedEffect: "reporting-worker releases its connections, and checkout-service's recovery signals return.",
      compensation: "Resume the reporting-worker batch once the incident is resolved.",
      policy: { version: "policy-2026.10.2", decision: "allows only a suggestion, which a person carries out", trace: ["pause-batch.reporting-worker: pause-batch on reporting-worker grants SuggestionOnly"] },
      approval: "suggestion-only",
    },
  },
};

// A freeze window or a rule outside its limits: the plan is shown, but nothing can approve it.
export const Denied: Story = {
  args: {
    plan: {
      ...rollback,
      policy: { version: "policy-2026.10.2", decision: "denies it", trace: ["freeze.release-week: reference is frozen until 18:00 UTC"] },
      approval: "denied",
    },
  },
};