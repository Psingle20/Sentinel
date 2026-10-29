"use client";

import { useActionState, useId } from "react";

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

import { type ControlState, emergencyStopAction, publishPolicyAction } from "./actions";

const idle: ControlState = { status: "idle" };

function Result({ state }: { state: ControlState }) {
  return state.status === "idle" ? null : (
    <p role={state.status === "error" ? "alert" : "status"} className="text-sm text-text-primary">
      {state.message}
    </p>
  );
}

// The emergency stop is independent of every model and workflow path (ADR-006): engaging it makes
// Executor deny every action, including approved ones that have not started.
export function EmergencyStop({ engaged }: { engaged: boolean }) {
  const [state, action, pending] = useActionState(emergencyStopAction, idle);

  return (
    <div className="flex flex-col gap-3">
      <Dialog>
        <DialogTrigger asChild>
          <Button variant={engaged ? "outline" : "destructive"} className="w-fit" disabled={pending}>
            {engaged ? "Release emergency stop" : "Engage emergency stop"}
          </Button>
        </DialogTrigger>
        <DialogContent>
          <form action={action} className="flex flex-col gap-4">
            <input type="hidden" name="engage" value={engaged ? "false" : "true"} />
            <DialogHeader>
              <DialogTitle>{engaged ? "Release the emergency stop?" : "Engage the emergency stop?"}</DialogTitle>
              <DialogDescription>
                {engaged
                  ? "Executor evaluates actions against policy again. Nothing that was denied runs by itself."
                  : "Executor denies every action until the stop is released. Your change is audited."}
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <DialogClose asChild>
                <Button type="button" variant="outline">
                  Cancel
                </Button>
              </DialogClose>
              <DialogClose asChild>
                <Button type="submit" variant={engaged ? "default" : "destructive"}>
                  {engaged ? "Release" : "Engage"}
                </Button>
              </DialogClose>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
      <Result state={state} />
    </div>
  );
}

// Policy as code, edited as its JSON document. Executor validates it and refuses a change based on
// an older version, so two operators cannot overwrite each other.
export function PolicyEditor({ document, version }: { document: string; version: string }) {
  const [state, action, pending] = useActionState(publishPolicyAction, idle);
  const fieldId = useId();

  return (
    <form action={action} className="flex flex-col gap-3">
      <input type="hidden" name="expectedVersion" value={version} />
      <label htmlFor={fieldId} className="text-sm font-medium text-text-primary">
        Policy document (give it a new version)
      </label>
      <Textarea id={fieldId} name="document" defaultValue={document} rows={16} className="font-mono text-xs" />
      <Button type="submit" className="w-fit" disabled={pending}>
        {pending ? "Publishing…" : "Publish policy"}
      </Button>
      <Result state={state} />
    </form>
  );
}
