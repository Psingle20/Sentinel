import type { ConfigurationArea } from "@/lib/api/contracts";

// What each configuration area covers and which service owns it (Phase 08 control plane).
export const areaTitles: Record<ConfigurationArea, string> = {
  runtime: "SLOs and workflow",
  models: "Models",
  sources: "Provider sources",
  api: "Tenants and notifications",
};

export const areaDescriptions: Record<ConfigurationArea, string> = {
  runtime:
    "Detection rules for SLO signals, the recovery signals each service must restore, the service catalog and owners, maintenance suppressions, which investigators run, workflow timers and consensus thresholds. Owned by Sentinel.Runtime; timers apply to new incident cycles.",
  models:
    "Which model provider serves each investigator role, the prompt version, the model budget per investigation, the shadow baseline and token prices. Owned by Sentinel.AgentWorker; only providers this deployment has credentials for can be selected.",
  sources:
    "The metric series, log streams and trace sources Ingestion pulls, and how often. Owned by Sentinel.Ingestion; provider endpoints and credentials stay in deployment configuration.",
  api: "Tenants and their environments, and notification channels and routes. Owned by Sentinel.Gateway; a webhook can target only an allowed host and names its signing secret's configuration key, never the secret.",
};

export function isConfigurationArea(value: string): value is ConfigurationArea {
  return value in areaTitles;
}
