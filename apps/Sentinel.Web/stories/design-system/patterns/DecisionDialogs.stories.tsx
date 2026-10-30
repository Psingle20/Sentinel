import type { Meta, StoryObj } from "@storybook/nextjs-vite";
import { expect, screen, userEvent, waitFor } from "storybook/test";

import { Field } from "@/design-system/patterns/field";
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
import { Textarea } from "@/design-system/primitives/textarea";

const confirmations = { Approve: "Confirm approval", Reject: "Confirm rejection", Revoke: "Confirm revocation" } as const;

// The decision dialogs as the incident page composes them, driven by keyboard and pointer (Phase E02). Submitting is
// the server's business, so these forms stop at the browser's own validation.
function DecisionDialog({ action, reason }: { action: keyof typeof confirmations; reason: boolean }) {
  const subject = action === "Revoke" ? "the approval of plan v3" : "plan v3";
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant={action === "Approve" ? "default" : "outline"}>{action === "Revoke" ? "Revoke approval" : `${action} plan`}</Button>
      </DialogTrigger>
      <DialogContent>
        <form className="flex flex-col gap-4" onSubmit={(event) => event.preventDefault()}>
          <DialogHeader>
            <DialogTitle>{`${action} ${subject}?`}</DialogTitle>
            <DialogDescription>
              {action === "Approve"
                ? "Your approval is signed for this exact plan content and can be used once."
                : "The incident is escalated to responders with your reason."}
            </DialogDescription>
          </DialogHeader>
          {reason ? <Field label="Reason">{(control) => <Textarea {...control} name="reason" required />}</Field> : null}
          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="outline">
                Cancel
              </Button>
            </DialogClose>
            <Button type="submit" variant={action === "Approve" ? "default" : "destructive"}>
              {confirmations[action]}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

const meta = {
  title: "Patterns/Decision dialogs",
  component: DecisionDialog,
  args: { action: "Approve", reason: false },
} satisfies Meta<typeof DecisionDialog>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ApproveOpensAndCancels: Story = {
  play: async () => {
    await userEvent.click(screen.getByRole("button", { name: "Approve plan" }));
    await expect(await screen.findByRole("dialog", { name: "Approve plan v3?" })).toBeVisible();

    await userEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    // Keyboard only: the trigger opens it again with Enter, and Escape closes it.
    screen.getByRole("button", { name: "Approve plan" }).focus();
    await userEvent.keyboard("{Enter}");
    await expect(await screen.findByRole("dialog", { name: "Approve plan v3?" })).toBeVisible();
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  },
};

export const RejectNeedsAReason: Story = {
  args: { action: "Reject", reason: true },
  play: async () => {
    await userEvent.click(screen.getByRole("button", { name: "Reject plan" }));
    const dialog = await screen.findByRole("dialog", { name: "Reject plan v3?" });

    await userEvent.click(screen.getByRole("button", { name: "Confirm rejection" }));
    const reason = screen.getByRole("textbox", { name: "Reason" });
    await expect(reason).toBeInvalid();
    await expect(dialog).toBeVisible();

    await userEvent.type(reason, "The rollback target is not the cause.");
    await expect(reason).toBeValid();
  },
};

export const RevokeByKeyboard: Story = {
  args: { action: "Revoke", reason: true },
  play: async () => {
    screen.getByRole("button", { name: "Revoke approval" }).focus();
    await userEvent.keyboard("{Enter}");
    await screen.findByRole("dialog", { name: "Revoke the approval of plan v3?" });

    const reason = screen.getByRole("textbox", { name: "Reason" });
    await waitFor(() => expect(reason).toHaveFocus());
    await userEvent.keyboard("A responder is already rolling back by hand.");
    await userEvent.tab();
    await expect(screen.getByRole("button", { name: "Cancel" })).toHaveFocus();
    await userEvent.tab();
    await expect(screen.getByRole("button", { name: "Confirm revocation" })).toHaveFocus();
  },
};
