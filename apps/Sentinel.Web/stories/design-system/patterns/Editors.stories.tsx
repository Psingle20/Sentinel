import type { Meta, StoryObj } from "@storybook/nextjs-vite";
import { expect, screen, userEvent } from "storybook/test";

import { ActionResult } from "@/design-system/patterns/action-result";
import { Field } from "@/design-system/patterns/field";
import { Button } from "@/design-system/primitives/button";
import { Input } from "@/design-system/primitives/input";
import { Textarea } from "@/design-system/primitives/textarea";

// An editor as the postmortem and control-plane pages compose one: labelled fields, a submit and the action's result
// (Phase E02). Saving is the server's business; this one reports a conflict, as a stale version does.
function Editor() {
  return (
    <form className="flex max-w-lg flex-col gap-3" onSubmit={(event) => event.preventDefault()}>
      <Field label="New version" description="The version this change publishes as.">
        {(control) => <Input {...control} required className="font-mono" />}
      </Field>
      <Field label="Narrative">{(control) => <Textarea {...control} rows={6} />}</Field>
      <Button type="submit" className="w-fit">
        Save narrative
      </Button>
      <ActionResult state={{ status: "error", message: "Someone else saved version 4 first. Reload to see it, then try again." }} />
    </form>
  );
}

const meta = {
  title: "Patterns/Editors",
  component: Editor,
} satisfies Meta<typeof Editor>;

export default meta;

type Story = StoryObj<typeof meta>;

export const KeyboardOnly: Story = {
  play: async () => {
    const version = screen.getByRole("textbox", { name: "New version" });
    version.focus();
    await userEvent.keyboard("2026-10-31.1");
    await userEvent.tab();
    await expect(screen.getByRole("textbox", { name: "Narrative" })).toHaveFocus();
    await userEvent.keyboard("The leak began with v2.4.1.");
    await userEvent.tab();
    await expect(screen.getByRole("button", { name: "Save narrative" })).toHaveFocus();

    await expect(version).toHaveAccessibleDescription("The version this change publishes as.");
    await expect(screen.getByRole("alert")).toHaveTextContent("Someone else saved version 4 first.");
  },
};
