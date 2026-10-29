import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { LiveIndicator } from "@/design-system/patterns/live-indicator";

const meta = {
  title: "Patterns/Live indicator",
  component: LiveIndicator,
  args: { state: "live" },
} satisfies Meta<typeof LiveIndicator>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Live: Story = { args: { state: "live" } };

export const Reconnecting: Story = { args: { state: "reconnecting" } };

export const Offline: Story = { args: { state: "offline" } };
