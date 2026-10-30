import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Select } from "@/design-system/primitives/select";

const meta = {
  title: "Primitives/Select",
  component: Select,
  args: {
    "aria-label": "Autonomy mode",
    defaultValue: "approval-required",
    children: (
      <>
        <option value="suggestion-only">Suggestion only</option>
        <option value="approval-required">Approval required</option>
        <option value="constrained">Constrained autonomous</option>
      </>
    ),
  },
} satisfies Meta<typeof Select>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Invalid: Story = {
  args: { "aria-invalid": true },
};

export const Disabled: Story = {
  args: { disabled: true },
};
