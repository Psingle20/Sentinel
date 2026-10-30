"use client";

import { Ban, CircleCheck } from "lucide-react";
import { useActionState, useEffect, useMemo, useState } from "react";

import { StatusBadge } from "@/design-system/domain/status-badge";
import { ActionResult } from "@/design-system/patterns/action-result";
import { Field } from "@/design-system/patterns/field";
import { Button } from "@/design-system/primitives/button";
import { Checkbox } from "@/design-system/primitives/checkbox";
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
import { Input } from "@/design-system/primitives/input";
import { Textarea } from "@/design-system/primitives/textarea";
import { type AdminArea, track } from "@/features/analytics/track";
import type { ConfigurationArea } from "@/lib/api/contracts";

import { actionOverrideAction, type ControlState, emergencyStopAction, publishConfigurationAction, publishPolicyAction } from "./actions";
import { parsed, type Setting, type SettingValue, settingsOf, withSettings } from "./settings";

const idle: ControlState = { status: "idle" };

// Counts each publish attempt by area and outcome, nothing more (the approved analytics catalog).
function useTrackPublish(area: AdminArea, state: ControlState) {
  useEffect(() => {
    if (state.status !== "idle") {
      track("configuration.published", { area, outcome: state.status === "done" ? "published" : "refused" });
    }
  }, [area, state]);
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
      <ActionResult state={state} />
    </div>
  );
}

// Policy as code, edited as its JSON document. Executor validates it and refuses a change based on
// an older version, so two operators cannot overwrite each other.
export function PolicyEditor({ document, version }: { document: string; version: string }) {
  const [state, action, pending] = useActionState(publishPolicyAction, idle);
  useTrackPublish("policy", state);

  return (
    <form action={action} className="flex flex-col gap-3">
      <input type="hidden" name="expectedVersion" value={version} />
      <Field label="Policy document (give it a new version)">
        {(control) => <Textarea {...control} name="document" defaultValue={document} rows={16} className="font-mono text-code" />}
      </Field>
      <Button type="submit" className="w-fit" disabled={pending}>
        {pending ? "Publishing…" : "Publish policy"}
      </Button>
      <ActionResult state={state} />
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
        <span className="font-mono text-body text-text-primary">{kind}</span>
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
      <ActionResult state={state} />
    </div>
  );
}

// A configuration area's document, edited as JSON. The owning service validates it and refuses a
// change based on an older version, exactly as Executor does for the policy. When the page shows a
// newer version, published here or from the settings, the text follows it: a stale document sent
// with the current version would quietly undo the other change.
export function ConfigurationEditor({ area, document, version }: { area: ConfigurationArea; document: string; version: string }) {
  const [state, action, pending] = useActionState(publishConfigurationAction.bind(null, area), idle);
  const [text, setText] = useState(document);
  const [shown, setShown] = useState(version);
  useTrackPublish(area, state);

  if (shown !== version) {
    setShown(version);
    setText(document);
  }

  return (
    <form action={action} className="flex flex-col gap-3">
      <input type="hidden" name="expectedVersion" value={version} />
      <Field label="Configuration document (give it a new version)">
        {(control) => <Textarea {...control} name="document" value={text} onChange={(event) => setText(event.target.value)} rows={20} className="font-mono text-code" />}
      </Field>
      <Button type="submit" className="w-fit" disabled={pending}>
        {pending ? "Publishing…" : "Publish configuration"}
      </Button>
      <ActionResult state={state} />
    </form>
  );
}

// A configuration area's single-value settings as fields, for the common changes; lists stay in the document editor. It
// publishes the whole document, so the owning service validates and versions it exactly as an edit of the document.
export function ConfigurationFields({ area, document, version }: { area: ConfigurationArea; document: Record<string, unknown>; version: string }) {
  const sections = useMemo(() => settingsOf(document), [document]);
  const [values, setValues] = useState(() => valuesOf(sections));
  const [nextVersion, setNextVersion] = useState("");
  const [shown, setShown] = useState(version);
  const [state, action, pending] = useActionState(publishConfigurationAction.bind(null, area), idle);
  useTrackPublish(area, state);

  // A newer version, published here or from the document, is what the next change builds on.
  if (shown !== version) {
    setShown(version);
    setValues(valuesOf(sections));
    setNextVersion("");
  }

  if (sections.length === 0) {
    return <p className="text-body text-text-muted">This area has no single-value settings; edit its document below.</p>;
  }

  return (
    <form action={action} className="flex flex-col gap-5">
      <input type="hidden" name="expectedVersion" value={version} />
      <input type="hidden" name="document" value={JSON.stringify(withSettings(document, values, nextVersion), null, 2)} />
      <Field label="New version">
        {(control) => (
          <Input
            {...control}
            required
            value={nextVersion}
            placeholder={`after ${version}`}
            onChange={(event) => setNextVersion(event.target.value)}
            className="max-w-sm font-mono"
          />
        )}
      </Field>
      {sections.map((section) => (
        <fieldset key={section.key} className="flex flex-col gap-3">
          <legend className="text-label text-text-primary">{section.title}</legend>
          <div className="grid gap-3 sm:grid-cols-2">
            {section.settings.map((setting) => (
              <SettingField
                key={setting.key}
                setting={setting}
                value={values[setting.key]}
                onChange={(value) => setValues((current) => ({ ...current, [setting.key]: value }))}
              />
            ))}
          </div>
        </fieldset>
      ))}
      <Button type="submit" className="w-fit" disabled={pending}>
        {pending ? "Publishing…" : "Publish settings"}
      </Button>
      <ActionResult state={state} />
    </form>
  );
}

function valuesOf(sections: ReturnType<typeof settingsOf>): Record<string, SettingValue> {
  return Object.fromEntries(sections.flatMap((section) => section.settings.map((setting) => [setting.key, setting.value])));
}

function SettingField({ setting, value, onChange }: { setting: Setting; value: SettingValue; onChange: (value: SettingValue) => void }) {
  if (typeof setting.value === "boolean") {
    return (
      <Field label={setting.label} orientation="horizontal">
        {(control) => <Checkbox {...control} checked={value === true} onChange={(event) => onChange(event.target.checked)} />}
      </Field>
    );
  }

  const numeric = typeof setting.value === "number";
  return (
    <Field label={setting.label}>
      {(control) => (
        <Input
          {...control}
          type={numeric ? "number" : "text"}
          step={numeric ? "any" : undefined}
          value={String(value)}
          onChange={(event) => onChange(parsed(setting.value, event.target.value))}
          className={numeric ? "tabular-nums" : "font-mono"}
        />
      )}
    </Field>
  );
}
