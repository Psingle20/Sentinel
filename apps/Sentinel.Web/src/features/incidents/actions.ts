"use server";

import { revalidatePath } from "next/cache";

import { ApiError, ApiUnavailableError, approvePlan, rejectPlan } from "@/lib/api/server";

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

function describe(error: unknown): string {
  if (error instanceof ApiError || error instanceof ApiUnavailableError) {
    return error.message;
  }
  return "The decision could not be sent. Try again.";
}
