import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { ConfidenceDisplay } from "@/design-system/domain/confidence-display";

const meta = {
  title: "Domain/Confidence display",
  component: ConfidenceDisplay,
  args: { confidence: 0.82, evidenceCompleteness: 0.9, calibration: "calibrated" },
  argTypes: {
    confidence: { control: { type: "range", min: 0, max: 1, step: 0.01 } },
    evidenceCompleteness: { control: { type: "range", min: 0, max: 1, step: 0.01 } },
    calibration: { control: "inline-radio", options: ["calibrated", "uncalibrated"] },
  },
} satisfies Meta<typeof ConfidenceDisplay>;

export default meta;

type Story = StoryObj<typeof meta>;

export const LeadingHypothesis: Story = {};

export const IncompleteEvidence: Story = {
  args: { confidence: 0.64, evidenceCompleteness: 0.4, calibration: "uncalibrated" },
};

export const Abstention: Story = {
  args: { confidence: 0.31, evidenceCompleteness: 0.55, calibration: "calibrated" },
};
