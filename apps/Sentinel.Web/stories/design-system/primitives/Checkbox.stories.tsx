import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Checkbox } from "@/design-system/primitives/checkbox";

const meta = {
  title: "Primitives/Checkbox",
  component: Checkbox,
  args: { "aria-label": "Shadow baseline" },
} satisfies Meta<typeof Checkbox>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Unchecked: Story = {};

export const Checked: Story = {
  args: { defaultChecked: true },
};

export const Disabled: Story = {
  args: { disabled: true, defaultChecked: true },
};
