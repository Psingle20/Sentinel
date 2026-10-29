import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { type ComparedHypothesis, HypothesisComparison } from "@/design-system/patterns/hypothesis-comparison";

const leak: ComparedHypothesis = {
  id: "h1",
  statement: "The checkout-service v2.4.1 deployment leaks Redis connections.",
  hypothesisKey: "change:d1",
  service: "checkout-service",
  standing: "leading",
  score: 1.15,
  support: 2,
  contradiction: 0,
  reason: "Stands.",
  challenges: [
    { falsifier: "change-serving-before-onset", verdict: "survived", detail: "v2.4.1 served from 20 s, before onset at 60 s." },
    { falsifier: "mechanism-follows-change", verdict: "survived", detail: "Connections rose only after v2.4.1 began serving." },
  ],
};

const orders: ComparedHypothesis = {
  id: "h2",
  statement: "An order-service regression slowed checkout.",
  hypothesisKey: "dependency-regression:order-service",
  service: "order-service",
  standing: "eliminated",
  score: -0.28,
  support: 1,
  contradiction: 1,
  reason: "Falsified: order-service latency did not rise before symptoms began.",
  challenges: [{ falsifier: "culprit-shift-precedes-onset", verdict: "falsified", detail: "order-service latency stayed flat." }],
};

const meta = {
  title: "Patterns/Hypothesis comparison",
  component: HypothesisComparison,
  args: { hypotheses: [leak, orders] },
} satisfies Meta<typeof HypothesisComparison>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Diagnosed: Story = {};

// Abstention with disagreement kept: no explanation is established, and the runner-up stays visible.
export const AbstainedWithDissent: Story = {
  args: {
    hypotheses: [
      { ...leak, standing: "standing", score: 0.62, support: 1, reason: "Only one independent evidence family supports it.", challenges: [] },
      { ...orders, standing: "dissent", score: 0.55, contradiction: 0, reason: "Within the dissent margin of the leader.", challenges: [] },
    ],
  },
};

export const Unchallenged: Story = {
  args: { hypotheses: [{ ...leak, standing: "standing", score: undefined, support: undefined, contradiction: undefined, reason: undefined, challenges: [] }] },
};
