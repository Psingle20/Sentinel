import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { RecoveryPanel } from "@/design-system/patterns/recovery-panel";

const meta = {
  title: "Patterns/Recovery panel",
  component: RecoveryPanel,
  args: {
    execution: { label: "Action verified at the target", detail: "checkout-service serves v2.4.0." },
    recovery: {
      status: "recovered",
      observedThrough: "09:06:30 UTC",
      measurements: [
        { criterion: "checkout-service slo_burn_rate < 1", observed: "0.2", met: true },
        { criterion: "checkout-service redis_connections < 20", observed: "6", met: true },
      ],
    },
  },
} satisfies Meta<typeof RecoveryPanel>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Recovered: Story = {};

export const CompensationRequired: Story = {
  args: {
    recovery: {
      status: "compensation-required",
      observedThrough: "09:06:30 UTC",
      reason: "Signals are worse after the action than before it.",
      measurements: [{ criterion: "checkout-service slo_burn_rate < 1", observed: "9", met: false }],
    },
  },
};

export const Inconclusive: Story = {
  args: {
    recovery: {
      status: "inconclusive",
      observedThrough: "09:05:10 UTC",
      reason: "Telemetry did not cover the window by the deadline.",
      measurements: [{ criterion: "checkout-service slo_burn_rate < 1", met: undefined }],
    },
  },
};

export const NotYetMeasured: Story = { args: { execution: { label: "Executing" }, recovery: undefined } };
