import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Field } from "@/design-system/patterns/field";
import { Checkbox } from "@/design-system/primitives/checkbox";
import { Input } from "@/design-system/primitives/input";
import { Textarea } from "@/design-system/primitives/textarea";

const meta = {
  title: "Patterns/Field",
  component: Field,
  args: {
    label: "Approval timeout",
    children: (control) => <Input {...control} defaultValue="00:30:00" />,
  },
  decorators: [
    (Story) => (
      <div className="max-w-sm">
        <Story />
      </div>
    ),
  ],
} satisfies Meta<typeof Field>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithDescription: Story = {
  args: { description: "How long a plan waits for an approver before the incident escalates." },
};

export const WithError: Story = {
  args: {
    description: "How long a plan waits for an approver before the incident escalates.",
    error: "Give a duration such as 00:30:00.",
    children: (control) => <Input {...control} defaultValue="half an hour" />,
  },
};

export const Multiline: Story = {
  args: { label: "Reason", children: (control) => <Textarea {...control} defaultValue="The rollback target is not the cause." /> },
};

export const HorizontalCheckbox: Story = {
  args: { label: "Shadow baseline enabled", orientation: "horizontal", children: (control) => <Checkbox {...control} defaultChecked /> },
};
