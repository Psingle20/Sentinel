import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { ApprovalState, approvalStatuses, type ApprovalStatus } from "@/design-system/domain/approval-state";
import {
  EvidenceDirectionBadge,
  evidenceDirections,
  type EvidenceDirection,
} from "@/design-system/domain/evidence-direction-badge";
import {
  IncidentStatusBadge,
  incidentStatuses,
  type IncidentStatus,
} from "@/design-system/domain/incident-status-badge";
import { RecoveryState, recoveryStatuses, type RecoveryStatus } from "@/design-system/domain/recovery-state";
import { SeverityIndicator, severities, type Severity } from "@/design-system/domain/severity-indicator";

function keysOf<T extends string>(record: Record<T, unknown>) {
  return Object.keys(record) as T[];
}

function StateRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2">
      <h2 className="text-xs font-semibold tracking-wide text-text-muted uppercase">{label}</h2>
      <div className="flex flex-wrap gap-2">{children}</div>
    </div>
  );
}

const meta = {
  title: "Domain/Status badges",
  component: IncidentStatusBadge,
  args: { status: "investigating" },
  argTypes: { status: { control: "select", options: keysOf(incidentStatuses) } },
} satisfies Meta<typeof IncidentStatusBadge>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Interactive: Story = {};

export const AllStates: Story = {
  render: () => (
    <div className="flex flex-col gap-6">
      <StateRow label="Incident status">
        {keysOf<IncidentStatus>(incidentStatuses).map((status) => (
          <IncidentStatusBadge key={status} status={status} />
        ))}
      </StateRow>
      <StateRow label="Severity">
        {keysOf<Severity>(severities).map((severity) => (
          <SeverityIndicator key={severity} severity={severity} />
        ))}
      </StateRow>
      <StateRow label="Evidence direction">
        {keysOf<EvidenceDirection>(evidenceDirections).map((direction) => (
          <EvidenceDirectionBadge key={direction} direction={direction} />
        ))}
      </StateRow>
      <StateRow label="Approval">
        {keysOf<ApprovalStatus>(approvalStatuses).map((status) => (
          <ApprovalState key={status} status={status} />
        ))}
      </StateRow>
      <StateRow label="Recovery">
        {keysOf<RecoveryStatus>(recoveryStatuses).map((status) => (
          <RecoveryState key={status} status={status} />
        ))}
      </StateRow>
    </div>
  ),
};
