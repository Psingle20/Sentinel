import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Input } from "@/design-system/primitives/input";
import { Label } from "@/design-system/primitives/label";

const meta = {
  title: "Primitives/Label",
  component: Label,
  args: { children: "Approval timeout", htmlFor: "label-story-control" },
  render: (args) => (
    <div className="flex max-w-sm flex-col gap-1.5">
      <Label {...args} />
      <Input id="label-story-control" defaultValue="00:30:00" />
    </div>
  ),
} satisfies Meta<typeof Label>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Default: Story = {};
