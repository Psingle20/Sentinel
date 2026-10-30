import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Input } from "@/design-system/primitives/input";

const meta = {
  title: "Primitives/Input",
  component: Input,
  args: { placeholder: "00:30:00", "aria-label": "Approval timeout" },
} satisfies Meta<typeof Input>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Filled: Story = {
  args: { defaultValue: "00:20:00" },
};

export const Numeric: Story = {
  args: { type: "number", step: "any", defaultValue: 0.5, "aria-label": "Abstention threshold" },
};

export const Invalid: Story = {
  args: { defaultValue: "twenty minutes", "aria-invalid": true },
};

export const Disabled: Story = {
  args: { disabled: true, defaultValue: "00:30:00" },
};
