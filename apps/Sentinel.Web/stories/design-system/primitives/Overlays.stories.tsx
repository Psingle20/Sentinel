import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { Button } from "@/design-system/primitives/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/design-system/primitives/dialog";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/design-system/primitives/tooltip";

const meta = {
  title: "Primitives/Overlays",
  component: Dialog,
} satisfies Meta<typeof Dialog>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ConfirmationDialog: Story = {
  render: () => (
    <Dialog defaultOpen>
      <DialogTrigger asChild>
        <Button>Approve plan</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Approve rollback?</DialogTitle>
          <DialogDescription>
            This approves plan version 3 exactly as shown. Any later change to the plan needs a new approval.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline">Cancel</Button>
          </DialogClose>
          <Button>Approve plan</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  ),
};

export const DialogTriggerOnly: Story = {
  render: () => (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="outline">Open dialog</Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Dialog title</DialogTitle>
          <DialogDescription>Opened with Enter or Space; Escape closes it and returns focus.</DialogDescription>
        </DialogHeader>
      </DialogContent>
    </Dialog>
  ),
};

export const OpenTooltip: Story = {
  render: () => (
    <div className="flex justify-center py-12">
      <Tooltip defaultOpen>
        <TooltipTrigger asChild>
          <Button variant="outline">Fencing token</Button>
        </TooltipTrigger>
        <TooltipContent>Incident stream version the action was based on.</TooltipContent>
      </Tooltip>
    </div>
  ),
};
