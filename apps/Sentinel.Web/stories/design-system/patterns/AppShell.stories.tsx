import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { IncidentStatusBadge } from "@/design-system/domain/incident-status-badge";
import { SeverityIndicator } from "@/design-system/domain/severity-indicator";
import { Button } from "@/design-system/primitives/button";
import { AppShell } from "@/design-system/patterns/app-shell";
import { DataState } from "@/design-system/patterns/data-state";
import { PageHeader } from "@/design-system/patterns/page-header";

const meta = {
  title: "Patterns/App shell",
  component: AppShell,
  parameters: { layout: "fullscreen" },
  args: { children: null },
} satisfies Meta<typeof AppShell>;

export default meta;

type Story = StoryObj<typeof meta>;

export const EmptyIncidents: Story = {
  render: () => (
    <AppShell>
      <PageHeader title="Incidents" description="Correlated incidents across monitored services." />
      <DataState
        kind="empty"
        title="No incidents yet"
        description="Incidents appear here when Sentinel correlates telemetry from a monitored service."
      />
    </AppShell>
  ),
};

export const IncidentHeader: Story = {
  render: () => (
    <AppShell>
      <PageHeader
        title="checkout-service latency and error budget burn"
        description="Opened 14:02 from checkout SLO burn and one linked deployment."
        actions={
          <>
            <SeverityIndicator severity="critical" />
            <IncidentStatusBadge status="awaiting-approval" />
            <Button>Review plan</Button>
          </>
        }
      />
    </AppShell>
  ),
};
