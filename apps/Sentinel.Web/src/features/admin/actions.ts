"use server";

import { revalidatePath } from "next/cache";

import type { ConfigurationArea } from "@/lib/api/contracts";
import { ApiError, ApiUnavailableError, publishConfiguration, publishPolicy, setActionDisabled, setEmergencyStop } from "@/lib/api/server";

export type ControlState = { status: "idle" | "done" | "error"; message?: string };

// Control-plane changes go Web -> Api, which authorizes the operator and audits the change;
// Executor owns and validates the setting (Phase 08).
export async function emergencyStopAction(_previous: ControlState, form: FormData): Promise<ControlState> {
  const engage = form.get("engage") === "true";
  try {
    await setEmergencyStop(engage);
    revalidatePath("/admin");
    return { status: "done", message: engage ? "Emergency stop engaged: Executor denies every action." : "Emergency stop released." };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

export async function publishPolicyAction(_previous: ControlState, form: FormData): Promise<ControlState> {
  let document: unknown;
  try {
    document = JSON.parse(String(form.get("document") ?? ""));
  } catch {
    return { status: "error", message: "The policy is not valid JSON." };
  }

  try {
    const published = await publishPolicy(document, String(form.get("expectedVersion")));
    revalidatePath("/admin");
    return { status: "done", message: `Published policy ${String(published.version)}.` };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

// Manual override (Phase 08): an operator disables one action kind at Executor, or enables it again.
export async function actionOverrideAction(_previous: ControlState, form: FormData): Promise<ControlState> {
  const action = String(form.get("action") ?? "");
  const disable = form.get("disable") === "true";
  try {
    await setActionDisabled(action, disable);
    revalidatePath("/admin");
    return { status: "done", message: disable ? `Executor now refuses every ${action} action.` : `${action} actions follow policy again.` };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

// Publishes a configuration area's document at the service that owns it; that service validates it.
export async function publishConfigurationAction(area: ConfigurationArea, _previous: ControlState, form: FormData): Promise<ControlState> {
  let document: unknown;
  try {
    document = JSON.parse(String(form.get("document") ?? ""));
  } catch {
    return { status: "error", message: "The configuration is not valid JSON." };
  }

  try {
    const published = await publishConfiguration(area, document, String(form.get("expectedVersion")));
    revalidatePath(`/admin/configuration/${area}`);
    return { status: "done", message: `Published version ${published.version}.` };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

function describe(error: unknown): string {
  if (error instanceof ApiError && error.status === 403) {
    return "Only operators can change the control plane.";
  }
  if (error instanceof ApiError || error instanceof ApiUnavailableError) {
    return error.message;
  }
  return "The change could not be sent. Try again.";
}
