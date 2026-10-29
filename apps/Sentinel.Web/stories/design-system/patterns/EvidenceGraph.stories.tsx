import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { EvidenceGraph, type GraphEdge, type GraphNode } from "@/design-system/patterns/evidence-graph";

// The golden incident: a deployment leaks Redis connections; the order-service explanation is contradicted.
const nodes: GraphNode[] = [
  { id: "change:d1", kind: "change", label: "checkout-service deploy v2.4.1" },
  { id: "service:checkout-service", kind: "service", label: "checkout-service" },
  { id: "service:order-service", kind: "service", label: "order-service" },
  { id: "hypothesis:leak", kind: "hypothesis", label: "The v2.4.1 deployment leaks Redis connections" },
  { id: "hypothesis:orders", kind: "hypothesis", label: "An order-service regression slowed checkout" },
  { id: "evidence:connections", kind: "evidence", label: "checkout Redis connections rose from 4 to 186" },
  { id: "evidence:serving", kind: "evidence", label: "v2.4.1 served from 20 s, before onset at 60 s" },
  { id: "evidence:latency", kind: "evidence", label: "order-service latency stayed flat" },
  { id: "symptom", kind: "symptom", label: "checkout-service slo_burn_rate at 14.8" },
];

const edges: GraphEdge[] = [
  { from: "change:d1", to: "service:checkout-service", relation: "changed" },
  { from: "hypothesis:leak", to: "change:d1", relation: "blames" },
  { from: "hypothesis:leak", to: "service:checkout-service", relation: "implicates" },
  { from: "hypothesis:orders", to: "service:order-service", relation: "implicates" },
  { from: "evidence:connections", to: "hypothesis:leak", relation: "supports" },
  { from: "evidence:serving", to: "hypothesis:leak", relation: "supports" },
  { from: "evidence:latency", to: "hypothesis:orders", relation: "contradicts" },
  { from: "hypothesis:leak", to: "symptom", relation: "explains" },
  { from: "hypothesis:orders", to: "symptom", relation: "explains" },
  { from: "symptom", to: "service:checkout-service", relation: "affects" },
];

const meta = {
  title: "Patterns/Evidence graph",
  component: EvidenceGraph,
  args: { nodes, edges, leadingIds: ["hypothesis:leak"], title: "Causal graph of the golden incident" },
} satisfies Meta<typeof EvidenceGraph>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Diagnosed: Story = {};

export const Undecided: Story = { args: { leadingIds: [] } };

// A large evidence set: columns cap their nodes and say how many are hidden.
export const LargeEvidenceSet: Story = {
  args: {
    nodes: [
      ...nodes,
      ...Array.from({ length: 14 }, (_, index): GraphNode => ({ id: `evidence:extra-${index}`, kind: "evidence", label: `Log signature ${index + 1} seen 42 times` })),
    ],
    edges: [
      ...edges,
      ...Array.from({ length: 14 }, (_, index): GraphEdge => ({ from: `evidence:extra-${index}`, to: "hypothesis:leak", relation: "supports" })),
    ],
  },
};
