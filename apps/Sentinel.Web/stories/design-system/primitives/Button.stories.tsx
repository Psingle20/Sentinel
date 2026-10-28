import type { Meta, StoryObj } from "@storybook/nextjs-vite";
import { ShieldCheck } from "lucide-react";

import { Button } from "@/design-system/primitives/button";

const meta = {
  title: "Primitives/Button",
  component: Button,
  args: { children: "Approve plan" },
  argTypes: {
    variant: { control: "select", options: ["default", "outline", "secondary", "ghost", "destructive", "link"] },
    size: { control: "select", options: ["default", "xs", "sm", "lg"] },
  },
} satisfies Meta<typeof Button>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Variants: Story = {
  render: (args) => (
    <div className="flex flex-wrap items-center gap-3">
      <Button {...args} variant="default">Approve plan</Button>
      <Button {...args} variant="outline">Review evidence</Button>
      <Button {...args} variant="secondary">Request changes</Button>
      <Button {...args} variant="ghost">Dismiss</Button>
      <Button {...args} variant="destructive">Reject plan</Button>
      <Button {...args} variant="link">View decision record</Button>
    </div>
  ),
};

export const Sizes: Story = {
  render: (args) => (
    <div className="flex flex-wrap items-center gap-3">
      <Button {...args} size="xs">Extra small</Button>
      <Button {...args} size="sm">Small</Button>
      <Button {...args} size="default">Default</Button>
      <Button {...args} size="lg">Large</Button>
    </div>
  ),
};

export const WithIcon: Story = {
  render: (args) => (
    <Button {...args}>
      <ShieldCheck data-icon="inline-start" aria-hidden="true" />
      Approve plan
    </Button>
  ),
};

export const Disabled: Story = {
  args: { disabled: true, children: "Approval pending policy evaluation" },
};
