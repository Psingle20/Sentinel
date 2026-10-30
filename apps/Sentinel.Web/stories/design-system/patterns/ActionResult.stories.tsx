import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { ActionResult } from "@/design-system/patterns/action-result";

const meta = {
  title: "Patterns/ActionResult",
  component: ActionResult,
  args: { state: { status: "done", message: "Saved as version 4." } },
} satisfies Meta<typeof ActionResult>;

export default meta;

type Story = StoryObj<typeof meta>;

export const Done: Story = {};

export const Failed: Story = {
  args: { state: { status: "error", message: "Someone else published version 4 first. Reload to see it, then try again." } },
};
