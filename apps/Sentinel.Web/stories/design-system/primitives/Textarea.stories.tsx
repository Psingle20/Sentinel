import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Textarea } from "@/design-system/primitives/textarea";

const meta = {
  title: "Primitives/Textarea",
  component: Textarea,
  args: { placeholder: "Why is this plan rejected?", "aria-label": "Rejection reason" },
} satisfies Meta<typeof Textarea>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Filled: Story = {
  args: { defaultValue: "Rolling back conflicts with the scheduled database migration." },
};

export const Invalid: Story = {
  args: { "aria-invalid": true },
};

export const Disabled: Story = {
  args: { disabled: true, defaultValue: "Decision already recorded." },
};
