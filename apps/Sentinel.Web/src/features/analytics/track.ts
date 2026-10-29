// Product analytics (Phase 08): the approved events and the only values each property may take.
// Api refuses anything else; docs/business-contracts/product-analytics.md describes the catalog.
// Nothing here identifies a person, an incident or a tenant, and nothing is free text.
type IncidentStatusValue = "detected" | "investigating" | "awaitingApproval" | "remediating" | "verifying" | "resolved" | "escalated";
type SeverityValue = "critical" | "high" | "medium" | "low";
export type AdminArea = "overview" | "policy" | "runtime" | "models" | "sources" | "api";

export type AnalyticsEvents = {
  "incident.viewed": { status: IncidentStatusValue; severity: SeverityValue };
  "approval.dialog-opened": { action: "approve" | "reject" | "revoke" };
  "postmortem.viewed": { status: IncidentStatusValue };
  "narrative.saved": { outcome: "saved" | "conflict" | "failed" };
  "replay.requested": { outcome: "reproduced" | "differs" | "failed" };
  "live.state-changed": { state: "live" | "reconnecting" | "offline" };
  "admin.viewed": { area: AdminArea };
  "configuration.published": { area: AdminArea; outcome: "published" | "refused" };
};

// Fire and forget: analytics never delays or breaks what the responder is doing.
export function track<Name extends keyof AnalyticsEvents>(name: Name, properties: AnalyticsEvents[Name]): void {
  if (typeof window === "undefined") {
    return;
  }

  const body = JSON.stringify({ name, properties });
  const sent = typeof navigator.sendBeacon === "function" && navigator.sendBeacon("/api/analytics", new Blob([body], { type: "application/json" }));
  if (!sent) {
    void fetch("/api/analytics", { method: "POST", body, keepalive: true, headers: { "Content-Type": "application/json" } }).catch(() => undefined);
  }
}
