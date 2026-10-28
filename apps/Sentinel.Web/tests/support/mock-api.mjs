// A stand-in for Sentinel.Api, used only by the web app's own Playwright tests. Next.js calls Api
// from the server, so browser-level request interception cannot reach those calls; this server
// answers them from a fixture instead. It is stateful: approving the plan moves it to approved.
//
// Usage: node tests/support/mock-api.mjs <port>

import { readFileSync } from "node:fs";
import { createServer } from "node:http";

const port = Number(process.argv[2] ?? 3199);
const fixture = JSON.parse(readFileSync(new URL("./fixtures/incident-awaiting-approval.json", import.meta.url), "utf8"));
let incident = structuredClone(fixture);

function send(response, status, body) {
  response.writeHead(status, { "Content-Type": "application/json" });
  response.end(body === undefined ? "" : JSON.stringify(body));
}

function readBody(request) {
  return new Promise((resolve) => {
    let data = "";
    request.on("data", (chunk) => (data += chunk));
    request.on("end", () => resolve(data ? JSON.parse(data) : {}));
  });
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://127.0.0.1:${port}`);
  const id = incident.summary.incidentId;
  const planId = incident.plan.plan.planId;

  if (request.method === "POST" && url.pathname === "/__reset") {
    incident = structuredClone(fixture);
    return send(response, 204);
  }

  if (request.method === "GET" && url.pathname === "/api/v1/incidents") {
    return send(response, 200, [incident.summary]);
  }

  if (request.method === "GET" && url.pathname === `/api/v1/incidents/${id}`) {
    return send(response, 200, incident);
  }

  if (request.method === "GET" && url.pathname.startsWith("/api/v1/incidents/")) {
    return send(response, 404, { title: "Incident.NotFound", status: 404 });
  }

  if (request.method === "POST" && url.pathname === `/api/v1/incidents/${id}/plans/${planId}/approve`) {
    const body = await readBody(request);
    if (body.planHash !== incident.plan.plan.planHash || body.planVersion !== incident.plan.plan.planVersion) {
      return send(response, 409, { title: "Approval.HashMismatch", detail: "The plan content changed since it was reviewed; review the current plan.", status: 409 });
    }

    if (incident.plan.status !== "awaitingApproval") {
      return send(response, 409, { title: "Approval.NotAwaitingApproval", detail: `The plan is ${incident.plan.status}, not awaiting approval.`, status: 409 });
    }

    const decidedAt = new Date("2026-10-28T09:09:00Z").toISOString();
    incident = {
      ...incident,
      summary: { ...incident.summary, status: "remediating" },
      plan: { ...incident.plan, status: "executing", approvedBy: "responder@example.test", approvedAt: decidedAt },
      execution: { planId, planVersion: 1, requestedAt: decidedAt },
    };
    return send(response, 202, {
      approvalId: "0192f0c4-0000-7000-8000-0000000000aa",
      decision: "approved",
      attestationId: "0192f0c4-0000-7000-8000-0000000000ab",
      decidedAt,
      expiresAt: "2026-10-28T09:24:00.0000000+00:00",
    });
  }

  if (request.method === "POST" && url.pathname === `/api/v1/incidents/${id}/plans/${planId}/reject`) {
    const body = await readBody(request);
    incident = {
      ...incident,
      summary: { ...incident.summary, status: "escalated" },
      plan: { ...incident.plan, status: "rejected" },
    };
    return send(response, 202, { approvalId: "0192f0c4-0000-7000-8000-0000000000ac", decision: "rejected", decidedAt: new Date().toISOString(), reason: body.reason });
  }

  return send(response, 404, { title: "Not found", status: 404 });
});

server.listen(port, "127.0.0.1", () => console.log(`Mock Sentinel.Api on http://127.0.0.1:${port}`));
