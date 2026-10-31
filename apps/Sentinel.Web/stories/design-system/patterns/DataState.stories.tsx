import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Button } from "@/design-system/primitives/button";
import { DataState } from "@/design-system/patterns/data-state";

const meta = {
  title: "Patterns/Data state",
  component: DataState,
  args: { kind: "empty", title: "No incidents yet" },
} satisfies Meta<typeof DataState>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Loading: Story = {
  args: { kind: "loading", title: "Loading incidents" },
};

export const Empty: Story = {
  args: {
    kind: "empty",
    title: "No incidents yet",
    description: "Incidents appear here when Sentinel correlates telemetry from a monitored service.",
  },
};

export const Error: Story = {
  args: {
    kind: "error",
    title: "Incidents could not be loaded",
    description: "Sentinel.Gateway did not respond. Retry, or check the service health dashboard.",
    action: <Button variant="outline">Retry</Button>,
  },
};

export const Stale: Story = {
  args: {
    kind: "stale",
    title: "Showing data from 4 minutes ago",
    description: "Live updates are reconnecting. The timeline will catch up automatically.",
  },
};

export const Degraded: Story = {
  args: {
    kind: "degraded",
    title: "Telemetry is incomplete",
    description: "One provider is unavailable, so recovery cannot be verified until it returns.",
  },
};

export const PermissionDenied: Story = {
  args: {
    kind: "permission-denied",
    title: "You cannot view this incident",
    description: "Ask a tenant administrator for access to the payments environment.",
  },
};
