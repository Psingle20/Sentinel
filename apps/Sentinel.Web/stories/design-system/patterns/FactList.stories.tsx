import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { FactList } from "@/design-system/patterns/fact-list";

const meta = {
  title: "Patterns/Fact list",
  component: FactList,
  args: {
    label: "Decision",
    facts: [
      {
        text: "Diagnosed by consensus-v1 at confidence 0.70 with evidence completeness 1.00: the checkout-service v2.4.1 deployment caused the symptoms.",
        at: "09:04:45 UTC",
        sources: [
          { kind: "event", ref: "16" },
          { kind: "decision", ref: "01a18325-9b89-7c5f-b061-0063e472dfd0" },
        ],
      },
      {
        text: "Supports: checkout-service Redis connections rose from 4 to 186.",
        at: "09:01:10 UTC",
        sources: [
          { kind: "evidence", ref: "01a18325-ca8a-7cf0-bdda-724a43ce12cb", href: "#evidence" },
          { kind: "observation", ref: "prometheus:redis_connections:checkout-service:1793200505" },
        ],
      },
    ],
  },
} satisfies Meta<typeof FactList>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Postmortem: Story = {};
