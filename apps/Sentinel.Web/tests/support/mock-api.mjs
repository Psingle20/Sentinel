// A stand-in for Sentinel.Api, used only by the web app's own Playwright tests. Next.js calls Api
// from the server, so browser-level request interception cannot reach those calls; this server
// answers them from fixtures instead. It is stateful: approving the plan moves it to approved,
// saving a narrative bumps its version, and every incident change is published on the live stream.
//
// Test-only controls, never part of Api's contract:
//   POST /__reset                     restore the fixtures
//   POST /__fail { route, status }    answer a route group with an error ("agent-executions", "similar", "admin")
//   POST /__advance { summary }       append a timeline entry, as Runtime would, and publish it live
//
// Usage: node tests/support/mock-api.mjs <port>

import { readFileSync } from "node:fs";
import { createServer } from "node:http";

const port = Number(process.argv[2] ?? 3199);
const fixture = (name) => JSON.parse(readFileSync(new URL(`./fixtures/${name}`, import.meta.url), "utf8"));
const incidentFixture = fixture("incident-awaiting-approval.json");
const historyFixture = fixture("incident-history.json");
const controlPlaneFixture = fixture("control-plane.json");

function initialState() {
  return {
    incident: structuredClone(incidentFixture),
    history: structuredClone(historyFixture),
    narrative: undefined,
    policy: structuredClone(controlPlaneFixture.policy),
    policyHistory: [{ version: controlPlaneFixture.policy.version, author: "repository", publishedAt: "2026-10-27T12:00:00+00:00", source: "file" }],
    controls: { emergencyStop: false, disabledActions: [] },
    audit: [],
    failures: new Map(),
  };
}

let state = initialState();

// Live events outlive a reset, as Api's sequence does: a reconnecting browser resumes after its last ID.
const liveEvents = [];
const liveClients = new Set();

function send(response, status, body) {
  response.writeHead(status, { "Content-Type": "application/json" });
  response.end(body === undefined ? "" : JSON.stringify(body));
}

function problem(response, status, title, detail) {
  return send(response, status, { title, detail, status });
}

function readBody(request) {
  return new Promise((resolve) => {
    let data = "";
    request.on("data", (chunk) => (data += chunk));
    request.on("end", () => resolve(data ? JSON.parse(data) : {}));
  });
}

function writeLive(response, live) {
  response.write(`id: ${live.sequence}\nevent: incident\ndata: ${JSON.stringify(live)}\n\n`);
}

function publish(kind, summary) {
  const { incident } = state;
  const live = {
    sequence: (liveEvents.at(-1)?.sequence ?? 0) + 1,
    tenantId: incident.summary.tenantId,
    environmentId: incident.summary.environmentId,
    incidentId: incident.summary.incidentId,
    kind,
    streamVersion: incident.summary.streamVersion,
    summary,
    severity: incident.summary.severity,
    occurredAt: new Date().toISOString(),
  };
  liveEvents.push(live);
  for (const client of liveClients) {
    writeLive(client, live);
  }
}

// Appends to the incident stream the way Runtime would: a new version, a timeline entry and a live event.
function append(kind, summary, change = {}) {
  const { incident } = state;
  const streamVersion = incident.summary.streamVersion + 1;
  state.incident = {
    ...incident,
    ...change,
    summary: { ...incident.summary, ...change.summary, streamVersion, updatedAt: new Date().toISOString() },
    timeline: [...(incident.timeline ?? []), { streamVersion, occurredAt: new Date().toISOString(), kind, summary }],
  };
  publish(kind, summary);
}

function audit(action, target, detail) {
  state.audit = [{ sequence: state.audit.length + 1, actor: "operator@example.test", action, target, detail, occurredAt: new Date().toISOString() }, ...state.audit];
}

function failure(group) {
  return state.failures.get(group);
}

async function handleIncident(request, response, url) {
  const { incident } = state;
  const id = incident.summary.incidentId;
  const planId = incident.plan.plan.planId;
  const base = `/api/v1/incidents/${id}`;

  if (request.method === "GET" && url.pathname === "/api/v1/incidents") {
    return send(response, 200, [incident.summary]);
  }

  if (url.pathname !== base && !url.pathname.startsWith(`${base}/`)) {
    return problem(response, 404, "Incident.NotFound", "The incident does not exist.");
  }

  const rest = url.pathname.slice(base.length);

  if (request.method === "GET" && rest === "") {
    return send(response, 200, incident);
  }

  if (request.method === "GET" && rest === "/agent-executions") {
    const status = failure("agent-executions");
    return status ? problem(response, status, "AgentWorker.Unavailable", "AgentWorker did not answer.") : send(response, 200, state.history.agentExecutions);
  }

  if (request.method === "GET" && rest === "/similar") {
    const status = failure("similar");
    return status ? problem(response, status, "Memory.Unavailable", "Incident memory did not answer.") : send(response, 200, state.history.similar);
  }

  if (request.method === "GET" && rest === "/postmortem") {
    return send(response, 200, { ...state.history.postmortem, status: incident.summary.status, narrative: state.narrative });
  }

  if (request.method === "PUT" && rest === "/postmortem/narrative") {
    const body = await readBody(request);
    if (body.expectedVersion !== (state.narrative?.version ?? 0)) {
      return problem(response, 409, "Postmortem.VersionConflict", "Someone else saved the narrative first; reload it and edit again.");
    }

    state.narrative = { markdown: body.markdown, version: body.expectedVersion + 1, editedBy: "responder@example.test", editedAt: new Date().toISOString() };
    return send(response, 200, state.narrative);
  }

  if (request.method === "POST" && rest === "/replays/domain") {
    const decision = incident.decision;
    return send(response, 200, {
      level: "domain",
      deterministic: true,
      incidentId: id,
      streamVersion: incident.summary.streamVersion,
      eventCount: incident.summary.streamVersion,
      stateHash: "sha256:3b8e5f2a7c1d9e4b6a0f8c2d5e7b9a1c3f5d7e9b0a2c4e6f8a1b3d5c7e9f0a2b",
      stateStable: true,
      projectionMatches: true,
      decisions: decision
        ? [
            {
              decisionId: decision.decisionId,
              streamVersion: 4,
              algorithmVersion: decision.algorithmVersion,
              settingsRecorded: true,
              recordedOutcome: decision.outcome,
              replayedOutcome: decision.outcome,
              recordedConfidence: decision.confidence,
              replayedConfidence: decision.confidence,
              matches: true,
            },
          ]
        : [],
      replayedAt: new Date().toISOString(),
    });
  }

  if (request.method === "POST" && rest === `/plans/${planId}/approve`) {
    const body = await readBody(request);
    if (body.planHash !== incident.plan.plan.planHash || body.planVersion !== incident.plan.plan.planVersion) {
      return problem(response, 409, "Approval.HashMismatch", "The plan content changed since it was reviewed; review the current plan.");
    }

    if (incident.plan.status !== "awaitingApproval") {
      return problem(response, 409, "Approval.NotAwaitingApproval", `The plan is ${incident.plan.status}, not awaiting approval.`);
    }

    // Approved and requested, but Executor has not confirmed the start: the approval can still be revoked.
    const decidedAt = new Date("2026-10-28T09:09:00Z").toISOString();
    append("plan.approved", "Plan v1 approved by responder@example.test; execution requested.", {
      summary: { status: "remediating" },
      plan: { ...incident.plan, status: "approved", approvedBy: "responder@example.test", approvedAt: decidedAt },
      execution: { planId, planVersion: 1, requestedAt: decidedAt },
    });
    return send(response, 202, {
      approvalId: "0192f0c4-0000-7000-8000-0000000000aa",
      decision: "approved",
      attestationId: "0192f0c4-0000-7000-8000-0000000000ab",
      decidedAt,
      expiresAt: "2026-10-28T09:24:00.0000000+00:00",
    });
  }

  if (request.method === "POST" && rest === `/plans/${planId}/reject`) {
    const body = await readBody(request);
    append("plan.rejected", `Plan v1 rejected: ${body.reason}`, {
      summary: { status: "escalated" },
      plan: { ...incident.plan, status: "rejected" },
    });
    return send(response, 202, { approvalId: "0192f0c4-0000-7000-8000-0000000000ac", decision: "rejected", decidedAt: new Date().toISOString(), reason: body.reason });
  }

  if (request.method === "POST" && rest === `/plans/${planId}/revoke`) {
    const body = await readBody(request);
    if (incident.plan.status !== "approved" || incident.execution?.startedAt) {
      return problem(response, 409, "Approval.NotRevocable", "Execution has started; the action runs to its verified outcome.");
    }

    append("approval.revoked", `Approval of plan v1 revoked: ${body.reason}`, {
      summary: { status: "escalated" },
      plan: { ...incident.plan, status: "rejected" },
      execution: undefined,
    });
    return send(response, 202, { approvalId: "0192f0c4-0000-7000-8000-0000000000ad", decision: "rejected", decidedAt: new Date().toISOString(), reason: body.reason });
  }

  return problem(response, 404, "Not found", undefined);
}

async function handleAdmin(request, response, url) {
  const status = failure("admin");
  if (status) {
    return problem(response, status, status === 403 ? "Forbidden" : "ControlPlane.Unavailable", status === 403 ? "The control plane is for operators." : "Executor did not answer.");
  }

  const path = url.pathname.slice("/api/v1/admin".length);

  if (request.method === "GET" && path === "/policy") {
    return send(response, 200, state.policy);
  }

  if (request.method === "GET" && path === "/policy/history") {
    return send(response, 200, state.policyHistory);
  }

  if (request.method === "PUT" && path === "/policy") {
    const body = await readBody(request);
    if (body.expectedVersion !== state.policy.version) {
      audit("policy.publish.refused", body.document?.version ?? "unknown", { reason: "version conflict" });
      return problem(response, 409, "Policy.VersionConflict", "The policy changed since it was read; reload it and apply the change again.");
    }

    if (!body.document?.version || body.document.version === state.policy.version) {
      audit("policy.publish.refused", String(body.document?.version ?? "unknown"), { reason: "version unchanged" });
      return problem(response, 400, "Policy.VersionUnchanged", "A published policy needs a new version.");
    }

    state.policy = body.document;
    state.policyHistory = [{ version: body.document.version, author: "operator@example.test", publishedAt: new Date().toISOString(), source: "control-plane" }, ...state.policyHistory];
    audit("policy.published", body.document.version, { previous: body.expectedVersion });
    return send(response, 200, { version: body.document.version });
  }

  if (request.method === "GET" && path === "/controls") {
    return send(response, 200, state.controls);
  }

  if (request.method === "POST" && path === "/controls/emergency-stop") {
    const body = await readBody(request);
    state.controls = { ...state.controls, emergencyStop: Boolean(body.enabled) };
    audit(body.enabled ? "emergency-stop.engaged" : "emergency-stop.released", "executor", { enabled: Boolean(body.enabled) });
    return send(response, 204);
  }

  if (request.method === "GET" && path === "/sources") {
    return send(response, 200, controlPlaneFixture.sources);
  }

  if (request.method === "GET" && path === "/audit") {
    return send(response, 200, state.audit);
  }

  return problem(response, 404, "Not found", undefined);
}

function handleEvents(request, response) {
  response.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" });
  response.write(": connected\n\n");
  const resume = Number(request.headers["last-event-id"]);
  if (Number.isInteger(resume) && resume > 0) {
    for (const live of liveEvents.filter((item) => item.sequence > resume)) {
      writeLive(response, live);
    }
  }

  liveClients.add(response);
  // The response closes when the browser (through Next.js) disconnects; the request closes once it is read.
  response.on("close", () => liveClients.delete(response));
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://127.0.0.1:${port}`);

  if (request.method === "POST" && url.pathname === "/__reset") {
    state = initialState();
    return send(response, 204);
  }

  if (request.method === "POST" && url.pathname === "/__fail") {
    const body = await readBody(request);
    state.failures.set(body.route, body.status ?? 503);
    return send(response, 204);
  }

  if (request.method === "POST" && url.pathname === "/__advance") {
    const body = await readBody(request);
    append("note.recorded", body.summary ?? "Runtime recorded a change.");
    return send(response, 204);
  }

  if (request.method === "GET" && url.pathname === "/api/v1/events") {
    return handleEvents(request, response);
  }

  if (url.pathname.startsWith("/api/v1/admin/")) {
    return handleAdmin(request, response, url);
  }

  if (url.pathname.startsWith("/api/v1/incidents")) {
    return handleIncident(request, response, url);
  }

  return problem(response, 404, "Not found", undefined);
});

server.listen(port, "127.0.0.1", () => console.log(`Mock Sentinel.Api on http://127.0.0.1:${port}`));
