import type { ApprovalReceipt, IncidentDetail, IncidentSummary, ProblemDetails } from "./contracts";

// Server-side access to Sentinel.Api: the only backend the web app talks to (ADR-001). The
// orchestrator provides Api's address; locally, requests carry a development identity, which
// Api accepts only in its Development environment (Phase 01A).

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | undefined,
  ) {
    super(problem?.detail ?? problem?.title ?? `Sentinel.Api returned ${status}.`);
  }
}

export class ApiUnavailableError extends Error {}

function baseUrl(): string {
  const url = process.env.services__api__http__0 ?? process.env.SENTINEL_API_URL;
  if (!url) {
    throw new ApiUnavailableError("Sentinel.Api's address is not configured (services__api__http__0 or SENTINEL_API_URL).");
  }
  return url.replace(/\/$/, "");
}

function headers(): HeadersInit {
  const user = process.env.SENTINEL_DEV_USER;
  return user ? { "X-Sentinel-User": user } : {};
}

async function send<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${baseUrl()}${path}`, {
      ...init,
      cache: "no-store",
      headers: { ...headers(), ...(init?.body ? { "Content-Type": "application/json" } : {}), ...init?.headers },
    });
  } catch (error) {
    throw new ApiUnavailableError(error instanceof Error ? error.message : "Sentinel.Api is unreachable.");
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined;
    throw new ApiError(response.status, problem);
  }

  return (await response.json()) as T;
}

export function listIncidents(): Promise<IncidentSummary[]> {
  return send<IncidentSummary[]>("/api/v1/incidents?limit=100");
}

export async function getIncident(incidentId: string): Promise<IncidentDetail | null> {
  try {
    return await send<IncidentDetail>(`/api/v1/incidents/${encodeURIComponent(incidentId)}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }
    throw error;
  }
}

export function approvePlan(incidentId: string, planId: string, planVersion: number, planHash: string): Promise<ApprovalReceipt> {
  return send<ApprovalReceipt>(`/api/v1/incidents/${encodeURIComponent(incidentId)}/plans/${encodeURIComponent(planId)}/approve`, {
    method: "POST",
    body: JSON.stringify({ planVersion, planHash }),
  });
}

export function rejectPlan(incidentId: string, planId: string, planVersion: number, reason: string): Promise<ApprovalReceipt> {
  return send<ApprovalReceipt>(`/api/v1/incidents/${encodeURIComponent(incidentId)}/plans/${encodeURIComponent(planId)}/reject`, {
    method: "POST",
    body: JSON.stringify({ planVersion, reason }),
  });
}
