import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Progress } from "@/design-system/primitives/progress";
import { Separator } from "@/design-system/primitives/separator";
import { Skeleton } from "@/design-system/primitives/skeleton";

const meta = {
  title: "Primitives/Feedback",
  component: Progress,
} satisfies Meta<typeof Progress>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ProgressValues: Story = {
  render: () => (
    <div className="flex max-w-sm flex-col gap-4">
      <Progress value={0} aria-label="Not started" />
      <Progress value={45} aria-label="In progress" />
      <Progress value={100} aria-label="Complete" />
    </div>
  ),
};

export const SkeletonLines: Story = {
  render: () => (
    <div className="flex max-w-sm flex-col gap-2" aria-hidden="true">
      <Skeleton className="h-4 w-3/4" />
      <Skeleton className="h-4 w-full" />
      <Skeleton className="h-4 w-2/3" />
    </div>
  ),
};

export const Separators: Story = {
  render: () => (
    <div className="flex max-w-sm flex-col gap-3 text-sm">
      <span>Observations</span>
      <Separator />
      <div className="flex h-5 items-center gap-3">
        <span>Evidence</span>
        <Separator orientation="vertical" />
        <span>Hypotheses</span>
      </div>
    </div>
  ),
};
