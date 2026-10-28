import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Badge } from "@/design-system/primitives/badge";

const meta = {
  title: "Primitives/Badge",
  component: Badge,
  args: { children: "checkout-service" },
} satisfies Meta<typeof Badge>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Variants: Story = {
  render: (args) => (
    <div className="flex flex-wrap items-center gap-3">
      <Badge {...args} variant="default">default</Badge>
      <Badge {...args} variant="secondary">secondary</Badge>
      <Badge {...args} variant="outline">outline</Badge>
      <Badge {...args} variant="destructive">destructive</Badge>
    </div>
  ),
};
