"use server";

import { revalidatePath } from "next/cache";

import type { DomainReplayResult } from "@/lib/api/contracts";
import { ApiError, ApiUnavailableError, approvePlan, rejectPlan, replayDomain, revokePlan, saveNarrative } from "@/lib/api/server";

export type DecisionState = { status: "idle" | "done" | "error"; message?: string };

// Approval goes Web -> Api, which signs the attestation and relays it to Runtime (ADR-006).
export async function approve(_previous: DecisionState, form: FormData): Promise<DecisionState> {
  const incidentId = String(form.get("incidentId"));
  try {
    const receipt = await approvePlan(incidentId, String(form.get("planId")), Number(form.get("planVersion")), String(form.get("planHash")));
    revalidatePath(`/incidents/${incidentId}`);
    return { status: "done", message: `Approved. Attestation ${receipt.attestationId} is valid until ${receipt.expiresAt}.` };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

export async function reject(_previous: DecisionState, form: FormData): Promise<DecisionState> {
  const incidentId = String(form.get("incidentId"));
  const reason = String(form.get("reason") ?? "").trim();
  if (!reason) {
    return { status: "error", message: "Give a reason for rejecting the plan." };
  }

  try {
    await rejectPlan(incidentId, String(form.get("planId")), Number(form.get("planVersion")), reason);
    revalidatePath(`/incidents/${incidentId}`);
    return { status: "done", message: "Rejected. The incident is escalated to responders." };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

// Withdraws an approval before execution starts (Phase 06); Runtime refuses once the start is confirmed.
export async function revoke(_previous: DecisionState, form: FormData): Promise<DecisionState> {
  const incidentId = String(form.get("incidentId"));
  const reason = String(form.get("reason") ?? "").trim();
  if (!reason) {
    return { status: "error", message: "Give a reason for revoking the approval." };
  }

  try {
    await revokePlan(incidentId, String(form.get("planId")), Number(form.get("planVersion")), reason);
    revalidatePath(`/incidents/${incidentId}`);
    return { status: "done", message: "Approval revoked. Nothing will execute; the incident is escalated to responders." };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

export type NarrativeState = DecisionState & { version?: number };

// Saves the postmortem narrative over the version the editor started from; a newer save wins and this one is refused.
export async function saveNarrativeAction(previous: NarrativeState, form: FormData): Promise<NarrativeState> {
  const incidentId = String(form.get("incidentId"));
  const expectedVersion = previous.version ?? Number(form.get("expectedVersion"));
  try {
    const saved = await saveNarrative(incidentId, String(form.get("markdown") ?? ""), expectedVersion);
    revalidatePath(`/incidents/${incidentId}/postmortem`);
    return { status: "done", message: `Saved as version ${saved.version}.`, version: saved.version };
  } catch (error) {
    return { status: "error", message: describe(error), version: expectedVersion };
  }
}

export type ReplayState = DecisionState & { result?: DomainReplayResult };

export async function replayAction(_previous: ReplayState, form: FormData): Promise<ReplayState> {
  try {
    const result = await replayDomain(String(form.get("incidentId")));
    const decisionsMatch = (result.decisions ?? []).every((decision) => decision.matches);
    const verdict = result.stateStable && result.projectionMatches && decisionsMatch ? "reproduced exactly" : "differs from what is stored";
    return { status: "done", message: `Replayed ${result.eventCount} events: the incident ${verdict}.`, result };
  } catch (error) {
    return { status: "error", message: describe(error) };
  }
}

function describe(error: unknown): string {
  if (error instanceof ApiError || error instanceof ApiUnavailableError) {
    return error.message;
  }
  return "The decision could not be sent. Try again.";
}
