"use client";

import { Ban, CircleCheck } from "lucide-react";
import { useActionState, useEffect, useId } from "react";

import { StatusBadge } from "@/design-system/domain/status-badge";
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
import { type AdminArea, track } from "@/features/analytics/track";
import type { ConfigurationArea } from "@/lib/api/contracts";

import { actionOverrideAction, type ControlState, emergencyStopAction, publishConfigurationAction, publishPolicyAction } from "./actions";

const idle: ControlState = { status: "idle" };

// Counts each publish attempt by area and outcome, nothing more (the approved analytics catalog).
function useTrackPublish(area: AdminArea, state: ControlState) {
  useEffect(() => {
    if (state.status !== "idle") {
      track("configuration.published", { area, outcome: state.status === "done" ? "published" : "refused" });
    }
  }, [area, state]);
}

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
  useTrackPublish("policy", state);

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

// Manual override (ADR-006): an operator takes one action kind out of service at Executor, whatever
// the policy permits, and puts it back. Like the emergency stop, it also refuses approved actions
// that have not started.
export function ActionOverrides({ kinds, disabled }: { kinds: readonly string[]; disabled: readonly string[] }) {
  return (
    <ul aria-label="Action overrides" className="flex flex-col gap-3">
      {kinds.map((kind) => (
        <li key={kind}>
          <ActionOverride kind={kind} disabled={disabled.includes(kind)} />
        </li>
      ))}
    </ul>
  );
}

function ActionOverride({ kind, disabled }: { kind: string; disabled: boolean }) {
  const [state, action, pending] = useActionState(actionOverrideAction, idle);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-mono text-sm text-text-primary">{kind}</span>
        <StatusBadge tone={disabled ? "critical" : "stable"} icon={disabled ? Ban : CircleCheck}>
          {disabled ? "Disabled by an operator" : "Follows policy"}
        </StatusBadge>
      </div>
      <Dialog>
        <DialogTrigger asChild>
          <Button variant="outline" size="sm" className="w-fit" disabled={pending}>
            {disabled ? `Enable ${kind}` : `Disable ${kind}`}
          </Button>
        </DialogTrigger>
        <DialogContent>
          <form action={action} className="flex flex-col gap-4">
            <input type="hidden" name="action" value={kind} />
            <input type="hidden" name="disable" value={disabled ? "false" : "true"} />
            <DialogHeader>
              <DialogTitle>{disabled ? `Enable ${kind} actions again?` : `Disable ${kind} actions?`}</DialogTitle>
              <DialogDescription>
                {disabled
                  ? `${kind} actions follow policy again. Nothing that was refused runs by itself.`
                  : `Executor refuses every ${kind} action until you enable it again, including approved ones that have not started. Your change is audited.`}
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <DialogClose asChild>
                <Button type="button" variant="outline">
                  Cancel
                </Button>
              </DialogClose>
              <DialogClose asChild>
                <Button type="submit" variant={disabled ? "default" : "destructive"}>
                  {disabled ? "Enable" : "Disable"}
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

// A configuration area's document, edited as JSON. The owning service validates it and refuses a
// change based on an older version, exactly as Executor does for the policy.
export function ConfigurationEditor({ area, document, version }: { area: ConfigurationArea; document: string; version: string }) {
  const [state, action, pending] = useActionState(publishConfigurationAction.bind(null, area), idle);
  const fieldId = useId();
  useTrackPublish(area, state);

  return (
    <form action={action} className="flex flex-col gap-3">
      <input type="hidden" name="expectedVersion" value={version} />
      <label htmlFor={fieldId} className="text-sm font-medium text-text-primary">
        Configuration document (give it a new version)
      </label>
      <Textarea id={fieldId} name="document" defaultValue={document} rows={20} className="font-mono text-xs" />
      <Button type="submit" className="w-fit" disabled={pending}>
        {pending ? "Publishing…" : "Publish configuration"}
      </Button>
      <Result state={state} />
    </form>
  );
}
