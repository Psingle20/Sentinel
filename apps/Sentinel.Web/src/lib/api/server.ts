import type {
  AgentExecution,
  AnalyticsCount,
  AnalyticsEvent,
  ConfigurationArea,
  DocumentState,
  DocumentVersion,
  EvidenceView,
  Page,
  TimelineEntry,
  ApprovalReceipt,
  AuditEntry,
  DomainReplayResult,
  ExecutorControls,
  IncidentDetail,
  IncidentSummary,
  PolicyVersionInfo,
  Postmortem,
  PostmortemNarrative,
  ProblemDetails,
  SimilarIncident,
  SourceState,
} from "./contracts";

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
  const url = process.env["services__gateway-service__http__0"] ?? process.env.SENTINEL_API_URL;
  if (!url) {
    throw new ApiUnavailableError("Sentinel.Gateway's address is not configured (services__gateway-service__http__0 or SENTINEL_API_URL).");
  }
  return url.replace(/\/$/, "");
}

function headers(): HeadersInit {
  const user = process.env.SENTINEL_DEV_USER;
  return user ? { "X-Sentinel-User": user } : {};
}

// Every mutation carries an idempotency key (Phase 08): one logical change, however often it is
// sent. A mutation that never reached Api is retried once with the same key, so a dropped
// connection cannot turn into a lost or a doubled approval.
async function request(path: string, init?: RequestInit): Promise<Response> {
  const mutation = init?.method !== undefined && init.method !== "GET";
  const key = mutation ? crypto.randomUUID() : undefined;
  for (let attempt = 1; ; attempt++) {
    try {
      return await fetch(`${baseUrl()}${path}`, {
        ...init,
        cache: "no-store",
        headers: {
          ...headers(),
          ...(init?.body ? { "Content-Type": "application/json" } : {}),
          ...(key ? { "Idempotency-Key": key } : {}),
          ...init?.headers,
        },
      });
    } catch (error) {
      if (!mutation || attempt >= 2) {
        throw new ApiUnavailableError(error instanceof Error ? error.message : "Sentinel.Api is unreachable.");
      }
    }
  }
}

async function send<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init);
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

export function revokePlan(incidentId: string, planId: string, planVersion: number, reason: string): Promise<ApprovalReceipt> {
  return send<ApprovalReceipt>(`/api/v1/incidents/${encodeURIComponent(incidentId)}/plans/${encodeURIComponent(planId)}/revoke`, {
    method: "POST",
    body: JSON.stringify({ planVersion, reason }),
  });
}

// Phase 07: agent activity, similar incidents, postmortems and replay.

function incidentPath(incidentId: string, rest = ""): string {
  return `/api/v1/incidents/${encodeURIComponent(incidentId)}${rest}`;
}

async function orNull<T>(request: Promise<T>): Promise<T | null> {
  try {
    return await request;
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null;
    }
    throw error;
  }
}

export function getAgentExecutions(incidentId: string): Promise<AgentExecution[]> {
  return send<AgentExecution[]>(incidentPath(incidentId, "/agent-executions"));
}

export async function getSimilarIncidents(incidentId: string): Promise<SimilarIncident[]> {
  return (await orNull(send<SimilarIncident[]>(incidentPath(incidentId, "/similar")))) ?? [];
}

export function getPostmortem(incidentId: string): Promise<Postmortem | null> {
  return orNull(send<Postmortem>(incidentPath(incidentId, "/postmortem")));
}

export function saveNarrative(incidentId: string, markdown: string, expectedVersion: number): Promise<PostmortemNarrative> {
  return send<PostmortemNarrative>(incidentPath(incidentId, "/postmortem/narrative"), {
    method: "PUT",
    body: JSON.stringify({ markdown, expectedVersion }),
  });
}

export function replayDomain(incidentId: string): Promise<DomainReplayResult> {
  return send<DomainReplayResult>(incidentPath(incidentId, "/replays/domain"), { method: "POST" });
}

// Phase 08: the control plane (operator role) and the live-update stream.

export function getPolicy(): Promise<Record<string, unknown>> {
  return send<Record<string, unknown>>("/api/v1/admin/policy");
}

export function getPolicyHistory(): Promise<PolicyVersionInfo[]> {
  return send<PolicyVersionInfo[]>("/api/v1/admin/policy/history");
}

export function publishPolicy(document: unknown, expectedVersion: string): Promise<Record<string, unknown>> {
  return send<Record<string, unknown>>("/api/v1/admin/policy", { method: "PUT", body: JSON.stringify({ document, expectedVersion }) });
}

export function getControls(): Promise<ExecutorControls> {
  return send<ExecutorControls>("/api/v1/admin/controls");
}

export async function setEmergencyStop(enabled: boolean): Promise<void> {
  await sendWithoutBody("/api/v1/admin/controls/emergency-stop", { method: "POST", body: JSON.stringify({ enabled }) });
}

export async function setActionDisabled(actionKind: string, disabled: boolean): Promise<void> {
  await sendWithoutBody(`/api/v1/admin/controls/actions/${encodeURIComponent(actionKind)}`, { method: "POST", body: JSON.stringify({ enabled: disabled }) });
}

export function getConfiguration(area: ConfigurationArea): Promise<DocumentState> {
  return send<DocumentState>(`/api/v1/admin/configuration/${area}`);
}

export function getConfigurationHistory(area: ConfigurationArea): Promise<DocumentVersion[]> {
  return send<DocumentVersion[]>(`/api/v1/admin/configuration/${area}/history`);
}

export function publishConfiguration(area: ConfigurationArea, document: unknown, expectedVersion: string): Promise<DocumentVersion> {
  return send<DocumentVersion>(`/api/v1/admin/configuration/${area}`, { method: "PUT", body: JSON.stringify({ document, expectedVersion }) });
}

export function getAnalytics(days = 30): Promise<AnalyticsCount[]> {
  return send<AnalyticsCount[]>(`/api/v1/admin/analytics?days=${days}`);
}

// Approved product-analytics events only; Api refuses anything outside its catalog.
export async function recordAnalytics(event: AnalyticsEvent): Promise<void> {
  await request("/api/v1/analytics", { method: "POST", body: JSON.stringify(event) });
}

export function getTimelinePage(incidentId: string, before?: string): Promise<Page<TimelineEntry>> {
  return send<Page<TimelineEntry>>(incidentPath(incidentId, `/timeline?limit=50${before ? `&before=${encodeURIComponent(before)}` : ""}`));
}

export function getEvidencePage(incidentId: string, offset?: string): Promise<Page<EvidenceView>> {
  return send<Page<EvidenceView>>(incidentPath(incidentId, `/evidence?limit=50${offset ? `&offset=${encodeURIComponent(offset)}` : ""}`));
}

export function getSources(): Promise<SourceState[]> {
  return send<SourceState[]>("/api/v1/admin/sources");
}

export function getAudit(): Promise<AuditEntry[]> {
  return send<AuditEntry[]>("/api/v1/admin/audit?limit=100");
}

// The live-update stream, proxied unchanged for the browser; Last-Event-ID resumes it after a reconnect.
export function openEventStream(lastEventId: string | null, signal: AbortSignal): Promise<Response> {
  return fetch(`${baseUrl()}/api/v1/events`, {
    cache: "no-store",
    signal,
    headers: { ...headers(), Accept: "text/event-stream", ...(lastEventId ? { "Last-Event-ID": lastEventId } : {}) },
  });
}

async function sendWithoutBody(path: string, init: RequestInit): Promise<void> {
  const response = await request(path, init);
  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as ProblemDetails | undefined;
    throw new ApiError(response.status, problem);
  }
}
