import AxeBuilder from "@axe-core/playwright";
import { test as base, expect, type Page } from "@playwright/test";

// Every page this suite opens is used only once React has hydrated it: a form filled before hydration races the wiring of
// its server action.
const test = base.extend({
  page: async ({ page }, provide) => {
    const goto = page.goto.bind(page);
    page.goto = async (url, options) => {
      const response = await goto(url, options);
      await page.locator("html[data-hydrated='true']").waitFor({ state: "attached" });
      return response;
    };
    await provide(page);
  },
});

const wcagTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
const mockApi = "http://127.0.0.1:3199";
const incidentId = "0192f0c4-0000-7000-8000-000000000001";
const incidentTitle = "checkout-service slo_burn_rate at 14.8";

async function openIncident(page: Page) {
  await page.getByRole("link", { name: incidentTitle }).click();
  await expect(page.getByRole("heading", { level: 1, name: incidentTitle })).toBeVisible();
}

test.describe("incidents", () => {
  test.beforeEach(async ({ page, request }) => {
    await request.post(`${mockApi}/__reset`);
    await page.goto("/");
  });

  test("lists correlated incidents with severity and status", async ({ page }) => {
    await expect(page.getByRole("heading", { level: 1, name: "Incidents" })).toBeVisible();
    const row = page.getByRole("row", { name: new RegExp(incidentTitle) });
    await expect(row).toContainText("checkout-service");
    await expect(row).toContainText("Critical");
    await expect(row).toContainText("Awaiting approval");
    await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  });

  test("offers a skip link as the first focus stop", async ({ page }) => {
    await page.keyboard.press("Tab");
    const skipLink = page.getByRole("link", { name: "Skip to content" });

    await expect(skipLink).toBeFocused();
    await skipLink.press("Enter");
    await expect(page).toHaveURL(/#main$/);
  });

  test("shows the decision, evidence and plan for an incident", { tag: "@layout" }, async ({ page }) => {
    await openIncident(page);

    await expect(page.getByText("Diagnosed", { exact: true })).toBeVisible();
    await expect(page.getByLabel("Decision checks")).toContainText("before symptom onset");
    await expect(page.getByRole("table", { name: "Evidence" })).toContainText("Redis connections rose from 4 to 186");
    await expect(page.getByText("Roll back checkout-service from v2.4.1 to v2.4.0").first()).toBeVisible();
    await expect(page.getByText("Approval required").first()).toBeVisible();
    await expect(page.getByLabel("Incident timeline")).toContainText("Plan v1 proposed");
  });

  test("shows the incident in the light theme, the responder's stored choice", { tag: "@layout" }, async ({ page }) => {
    await page.addInitScript(() => window.localStorage.setItem("sentinel-theme", "light"));
    await page.goto("/");
    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);

    await openIncident(page);

    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);
    await expect(page.getByRole("combobox", { name: "Theme" })).toHaveValue("light");
    await expect(page.getByText("Diagnosed", { exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Evidence" })).toContainText("Redis connections rose from 4 to 186");
    const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test("switches the theme from the header and keeps the choice", async ({ page }) => {
    const theme = page.getByRole("combobox", { name: "Theme" });
    await expect(theme).toHaveValue("dark");

    await theme.selectOption("light");
    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);
    await page.reload();
    await expect(page.locator("html")).not.toHaveClass(/\bdark\b/);
    await expect(page.getByRole("combobox", { name: "Theme" })).toHaveValue("light");

    await page.getByRole("combobox", { name: "Theme" }).selectOption("dark");
    await expect(page.locator("html")).toHaveClass(/\bdark\b/);
  });

  test("compares the competing hypotheses and draws the evidence graph", async ({ page }) => {
    await openIncident(page);

    const comparison = page.getByRole("table", { name: "Hypothesis comparison" });
    await expect(comparison).toContainText("introduced a Redis connection leak");
    await expect(comparison).toContainText("A change in load on checkout-service");
    await expect(comparison).toContainText("Request rate on checkout-service stayed flat");
    await expect(page.getByRole("img", { name: `Causal graph of ${incidentTitle}` })).toBeVisible();
    await expect(page.getByLabel("Correlated changes")).toContainText("checkout-service deployment v2.4.1");
  });

  test("shows which investigators ran and what is remembered", async ({ page }) => {
    await openIncident(page);

    const activity = page.getByRole("list", { name: "Agent activity" });
    await expect(activity).toContainText("sentinel-test-double/deterministic-analyst");
    await expect(activity).toContainText("deploy-blame baseline");
    await expect(page.getByRole("list", { name: "Similar incidents" })).toContainText("exhausted its Redis connection pool");
  });

  test("keeps the incident visible when AgentWorker and memory are down", async ({ page, request }) => {
    await request.post(`${mockApi}/__fail`, { data: { route: "agent-executions", status: 503 } });
    await request.post(`${mockApi}/__fail`, { data: { route: "similar", status: 503 } });
    await openIncident(page);

    await expect(page.getByText("Agent activity is unavailable")).toBeVisible();
    await expect(page.getByText("Incident memory is unavailable")).toBeVisible();
    await expect(page.getByText("Diagnosed", { exact: true })).toBeVisible();
  });

  test("approves the exact plan version after confirmation", async ({ page }) => {
    await openIncident(page);
    await page.getByRole("button", { name: "Approve plan" }).click();

    const dialog = page.getByRole("dialog", { name: "Approve plan v1?" });
    await expect(dialog).toContainText("sha256:6f1c2b9e");
    await dialog.getByRole("button", { name: "Confirm approval" }).click();

    // The page re-renders from Api: the plan is approved and the incident is remediating.
    await expect(page.getByText(/^Approved by responder@example\.test at /)).toBeVisible();
    await expect(page.getByText("Remediating", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve plan" })).toHaveCount(0);
  });

  test("approves a poison message's quarantine, which names its queue and limits instead of a rollback", async ({ page, request }) => {
    await request.post(`${mockApi}/__quarantine`);
    await page.reload();
    await openIncident(page);

    await expect(page.getByText("Quarantine the messages of queue-consumer's orders queue received at least 3 times").first()).toBeVisible();
    await expect(page.getByText("Release the quarantined messages back to orders.")).toBeVisible();
    await page.getByRole("button", { name: "Approve plan" }).click();
    await page.getByRole("dialog", { name: "Approve plan v1?" }).getByRole("button", { name: "Confirm approval" }).click();

    await expect(page.getByText(/^Approved by responder@example\.test at /)).toBeVisible();
    await expect(page.getByText("Remediating", { exact: true })).toBeVisible();
  });

  test("revokes an approval before execution starts", async ({ page }) => {
    await openIncident(page);
    await page.getByRole("button", { name: "Approve plan" }).click();
    await page.getByRole("dialog", { name: "Approve plan v1?" }).getByRole("button", { name: "Confirm approval" }).click();

    await page.getByRole("button", { name: "Revoke approval" }).click();
    const dialog = page.getByRole("dialog", { name: "Revoke the approval of plan v1?" });
    await dialog.getByLabel("Reason").fill("The release train was rescheduled.");
    await dialog.getByRole("button", { name: "Confirm revocation" }).click();

    await expect(page.getByText("Escalated", { exact: true })).toBeVisible();
    await expect(page.getByLabel("Incident timeline")).toContainText("Approval of plan v1 revoked");
    await expect(page.getByRole("button", { name: "Revoke approval" })).toHaveCount(0);
  });

  test("requires a reason to reject a plan", async ({ page }) => {
    await openIncident(page);
    await page.getByRole("button", { name: "Reject plan" }).click();

    const dialog = page.getByRole("dialog", { name: "Reject plan v1?" });
    await dialog.getByLabel("Reason").fill("Conflicts with a scheduled migration.");
    await dialog.getByRole("button", { name: "Confirm rejection" }).click();

    await expect(page.getByText("Escalated", { exact: true })).toBeVisible();
    await expect(page.getByText("Rejected", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Reject plan" })).toHaveCount(0);
  });

  test("updates the incident live as Runtime records changes", async ({ page, request }) => {
    await openIncident(page);
    await expect(page.getByRole("status").filter({ hasText: /^Live$/ })).toBeVisible();

    await request.post(`${mockApi}/__advance`, { data: { summary: "Runtime recorded a live change for this test." } });

    // No navigation or reload: the live event makes the page refetch from the query API.
    await expect(page.getByLabel("Incident timeline")).toContainText("Runtime recorded a live change for this test.");
  });

  test("counts an incident view by its status and severity only", async ({ page, request }) => {
    await openIncident(page);
    await page.getByRole("button", { name: "Approve plan" }).click();

    await expect
      .poll(async () => (await request.get(`${mockApi}/__analytics`)).json())
      .toEqual(
        expect.arrayContaining([
          { name: "incident.viewed", properties: { status: "awaitingApproval", severity: "critical" } },
          { name: "approval.dialog-opened", properties: { action: "approve" } },
        ]),
      );
  });

  test("reports an unknown incident without failing the page", async ({ page }) => {
    await page.goto("/incidents/0192f0c4-0000-7000-8000-00000000dead");

    await expect(page.getByRole("status")).toContainText("Incident not found");
  });
});

// Phase 08 performance: a long incident, 5,000 timeline entries and 400 evidence items, each also a node of the causal
// graph. The page must be usable within its budget and put only a page of the timeline and evidence in the document.
test.describe("large incidents", () => {
  test.beforeEach(async ({ request }) => {
    await request.post(`${mockApi}/__reset`);
    await request.post(`${mockApi}/__large`, { data: { timeline: 5000, evidence: 400 } });
  });

  test("are usable within the budget and page their timeline and evidence", async ({ page }) => {
    const started = Date.now();
    await page.goto(`/incidents/${incidentId}`);
    await expect(page.getByRole("heading", { level: 1, name: incidentTitle })).toBeVisible();
    await expect(page.getByRole("img", { name: `Causal graph of ${incidentTitle}` })).toBeVisible();
    expect(Date.now() - started, "time until the incident and its graph are on screen").toBeLessThan(6_000);

    const timeline = page.getByRole("list", { name: "Incident timeline" }).getByRole("listitem");
    await expect(timeline).toHaveCount(50);
    await expect(timeline.last()).toContainText("v5000");
    await page.getByRole("button", { name: "Show earlier entries (4950 more)" }).click();
    await expect(timeline).toHaveCount(100);
    await expect(timeline.first()).toContainText("v4901");

    const rows = page.getByRole("table", { name: "Evidence" }).getByRole("row");
    await expect(rows).toHaveCount(51);
    await page.getByRole("button", { name: "Show 50 more of 352 remaining" }).click();
    await expect(rows).toHaveCount(101);
  });

  test("reveal evidence a link points to beyond the first page", async ({ page }) => {
    const evidenceId = "0192f0c4-0000-7000-8000-000000100300";

    await page.goto(`/incidents/${incidentId}#evidence-${evidenceId}`);

    await expect(page.locator(`[id="evidence-${evidenceId}"]`)).toBeInViewport();
    await expect(page.locator(`[id="evidence-${evidenceId}"]`)).toContainText("Signal 300");
  });
});

test.describe("postmortem", () => {
  test.beforeEach(async ({ page, request }) => {
    await request.post(`${mockApi}/__reset`);
    await page.goto(`/incidents/${incidentId}`);
    await page.getByRole("link", { name: "Postmortem and replay" }).click();
    await expect(page.getByRole("heading", { level: 1, name: `Postmortem: ${incidentTitle}` })).toBeVisible();
  });

  test("lists sourced facts that link back to the evidence", async ({ page }) => {
    await expect(page.getByRole("list", { name: "Decision" })).toContainText("Diagnosed by consensus-v1");
    await expect(page.getByRole("list", { name: "Decision" })).toContainText("Eliminated: a change in load");

    await page.getByRole("list", { name: "Evidence" }).getByRole("link").first().click();
    await expect(page).toHaveURL(new RegExp(`/incidents/${incidentId}#evidence-0192f0c4-0000-7000-8000-000000000005$`));
  });

  test("saves the narrative with its version", async ({ page }) => {
    const narrative = page.getByRole("textbox", { name: "Narrative" });
    await narrative.fill("A deployment leaked Redis connections; we add a pool-size alert.");
    await page.getByRole("button", { name: "Save narrative" }).click();
    await expect(page.getByText("Saved as version 1.")).toBeVisible();

    await narrative.fill("A deployment leaked Redis connections; we add a pool-size alert and a canary.");
    await page.getByRole("button", { name: "Save narrative" }).click();
    await expect(page.getByText("Saved as version 2.")).toBeVisible();
  });

  test("refuses to overwrite a narrative someone else saved", async ({ page, request }) => {
    await request.put(`${mockApi}/api/v1/incidents/${incidentId}/postmortem/narrative`, { data: { markdown: "Written elsewhere.", expectedVersion: 0 } });

    await page.getByRole("textbox", { name: "Narrative" }).fill("My version of events.");
    await page.getByRole("button", { name: "Save narrative" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "Someone else saved the narrative first" })).toBeVisible();
  });

  test("replays the incident from its event stream", async ({ page }) => {
    await page.getByRole("button", { name: "Replay from the event stream" }).click();

    await expect(page.getByText(/Replayed \d+ events: the incident reproduced exactly\./)).toBeVisible();
    await expect(page.getByLabel("Replay checks")).toContainText("recorded diagnosed, replayed diagnosed");
  });
});

test.describe("control plane", () => {
  test.beforeEach(async ({ request }) => {
    await request.post(`${mockApi}/__reset`);
  });

  test("is reachable from the incident list", { tag: "@layout" }, async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "Control plane" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Control plane" })).toBeVisible();
    await expect(page.getByText("Remediation policy policy-2026.10.2")).toBeVisible();
    const sources = page.getByRole("table", { name: "Source health" });
    await expect(page.getByText("2 healthy, 1 degraded, 0 unavailable.")).toBeVisible();
    await expect(sources.getByRole("row").nth(1), "the failing source comes first").toContainText("loki:logs:warnings");
    await expect(sources.getByRole("row").nth(1)).toContainText("2 in a row");
    await expect(sources.getByRole("row").nth(1)).toContainText("Loki answered 503: the ingester is not ready.");
    await expect(sources.getByRole("row").nth(1)).toContainText("90 s behind its last pull");
  });

  test("moves between its pages and marks the current one", { tag: "@layout" }, async ({ page }) => {
    await page.goto("/admin");
    const navigation = page.getByRole("navigation", { name: "Control plane" });
    await expect(navigation.getByRole("link", { name: "Overview" })).toHaveAttribute("aria-current", "page");

    await navigation.getByRole("link", { name: "Models" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Models" })).toBeVisible();
    await expect(navigation.getByRole("link", { name: "Models" })).toHaveAttribute("aria-current", "page");
    await expect(navigation.getByRole("link", { name: "Overview" })).not.toHaveAttribute("aria-current", "page");
  });

  test("engages the emergency stop and audits it", async ({ page }) => {
    await page.goto("/admin");
    await page.getByRole("button", { name: "Engage emergency stop" }).click();
    await page.getByRole("dialog", { name: "Engage the emergency stop?" }).getByRole("button", { name: "Engage", exact: true }).click();

    await expect(page.getByText("Emergency stop engaged", { exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("emergency-stop.engaged");
    await expect(page.getByRole("button", { name: "Release emergency stop" })).toBeVisible();
  });

  test("takes one action kind out of service and puts it back", async ({ page }) => {
    await page.goto("/admin");
    const overrides = page.getByRole("list", { name: "Action overrides" });
    const rollback = overrides.getByRole("listitem").filter({ has: page.getByText("rollback", { exact: true }) });
    await expect(rollback).toContainText("Follows policy");

    await rollback.getByRole("button", { name: "Disable rollback" }).click();
    await page.getByRole("dialog", { name: "Disable rollback actions?" }).getByRole("button", { name: "Disable", exact: true }).click();

    await expect(page.getByText("Executor now refuses every rollback action.")).toBeVisible();
    await expect(rollback).toContainText("Disabled by an operator");
    await expect(overrides.getByRole("listitem").filter({ has: page.getByText("pause-batch", { exact: true }) })).toContainText("Follows policy");
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("controls.action.disable");

    await rollback.getByRole("button", { name: "Enable rollback" }).click();
    await page.getByRole("dialog", { name: "Enable rollback actions again?" }).getByRole("button", { name: "Enable", exact: true }).click();

    await expect(page.getByText("rollback actions follow policy again.")).toBeVisible();
    await expect(rollback).toContainText("Follows policy");
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("controls.action.enable");
  });

  test("publishes a new policy version", async ({ page }) => {
    await page.goto("/admin/policy");
    const editor = page.getByRole("textbox", { name: "Policy document (give it a new version)" });
    const document = await editor.inputValue();
    await editor.fill(document.replace('"policy-2026.10.2"', '"policy-2026.10.3"'));
    await page.getByRole("button", { name: "Publish policy" }).click();

    await expect(page.getByText("Published policy policy-2026.10.3.")).toBeVisible();
    await expect(page.getByRole("list", { name: "Policy versions" })).toContainText("policy-2026.10.3");

    await page.getByRole("navigation", { name: "Control plane" }).getByRole("link", { name: "Overview" }).click();
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("policy.published");
  });

  test("refuses a policy without a new version", async ({ page }) => {
    await page.goto("/admin/policy");
    await page.getByRole("button", { name: "Publish policy" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "A published policy needs a new version." })).toBeVisible();
  });

  test("publishes a configuration area's settings, each value keeping its type", async ({ page }) => {
    await page.goto("/admin/configuration/models");
    const general = page.getByRole("group", { name: "General" });
    await expect(general.getByLabel("Max model turns")).toHaveValue("6");

    await page.getByLabel("New version", { exact: true }).fill("models-2026.10.2");
    await general.getByLabel("Max model turns").fill("8");
    await general.getByLabel("Shadow baseline").uncheck();
    await page.getByRole("button", { name: "Publish settings" }).click();

    await expect(page.getByText("Published version models-2026.10.2.")).toBeVisible();
    const document = page.getByRole("textbox", { name: "Configuration document (give it a new version)" });
    await expect(document).toHaveValue(/"maxModelTurns": 8,/);
    await expect(document).toHaveValue(/"shadowBaseline": false,/);
    await expect(page.getByLabel("New version", { exact: true })).toHaveValue("");
  });

  test("publishes a setting from its section and leaves the lists alone", async ({ page }) => {
    await page.goto("/admin/configuration/runtime");
    await page.getByLabel("New version", { exact: true }).fill("runtime-2026.10.2");
    await page.getByRole("group", { name: "Workflow" }).getByLabel("Approval timeout").fill("00:20:00");
    await page.getByRole("button", { name: "Publish settings" }).click();

    await expect(page.getByText("Published version runtime-2026.10.2.")).toBeVisible();
    const document = page.getByRole("textbox", { name: "Configuration document (give it a new version)" });
    await expect(document).toHaveValue(/"approvalTimeout": "00:20:00"/);
    await expect(document).toHaveValue(/"metric": "slo_burn_rate"/);
    await expect(page.getByRole("list", { name: "Configuration versions" })).toContainText("runtime-2026.10.2");
  });

  test("publishes a configuration area over the version it was read at", async ({ page }) => {
    await page.goto("/admin/configuration/runtime");
    await expect(page.getByRole("heading", { level: 1, name: "SLOs and workflow" })).toBeVisible();
    await expect(page.getByText("Version runtime-2026.10.1")).toBeVisible();
    await expect(page.getByText("The deployment's defaults: nothing has been published from the control plane yet.")).toBeVisible();
    await expect(page.getByRole("list", { name: "Configuration versions" })).toContainText("Deployment defaults");

    const editor = page.getByRole("textbox", { name: "Configuration document (give it a new version)" });
    const document = await editor.inputValue();
    await editor.fill(document.replace('"runtime-2026.10.1"', '"runtime-2026.10.2"').replace('"approvalTimeout": "00:30:00"', '"approvalTimeout": "00:20:00"'));
    await page.getByRole("button", { name: "Publish configuration" }).click();

    await expect(page.getByText("Published version runtime-2026.10.2.")).toBeVisible();
    await expect(page.getByText("Version runtime-2026.10.2", { exact: true })).toBeVisible();
    await expect(page.getByText(/^Published by operator@example\.test at .*, based on runtime-2026\.10\.1\.$/)).toBeVisible();
    await expect(page.getByRole("list", { name: "Configuration versions" })).toContainText("runtime-2026.10.2");
    await expect(editor).toHaveValue(/"approvalTimeout": "00:20:00"/);

    await page.getByRole("navigation", { name: "Control plane" }).getByRole("link", { name: "Overview" }).click();
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("runtime/runtime-2026.10.2");
  });

  test("refuses a configuration someone else changed since it was read", async ({ page, request }) => {
    await page.goto("/admin/configuration/models");
    const editor = page.getByRole("textbox", { name: "Configuration document (give it a new version)" });
    const document = await editor.inputValue();

    const current = (await (await request.get(`${mockApi}/api/v1/admin/configuration/models`)).json()) as { document: Record<string, unknown> };
    await request.put(`${mockApi}/api/v1/admin/configuration/models`, {
      data: { document: { ...current.document, version: "models-2026.10.2" }, expectedVersion: "models-2026.10.1" },
    });

    await editor.fill(document.replace('"models-2026.10.1"', '"models-2026.10.3"'));
    await page.getByRole("button", { name: "Publish configuration" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "The configuration changed since it was read" })).toBeVisible();
  });

  test("refuses a configuration without a new version", async ({ page }) => {
    await page.goto("/admin/configuration/sources");
    await page.getByRole("button", { name: "Publish configuration" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "A published configuration needs a new version." })).toBeVisible();
  });

  test("summarizes what a configuration area holds", async ({ page }) => {
    await page.goto("/admin/configuration/api");

    await expect(page.getByRole("heading", { level: 1, name: "Tenants and notifications" })).toBeVisible();
    await expect(page.getByRole("definition").filter({ hasText: "1 entry" })).toHaveCount(3);
  });

  test("reports an unknown configuration area", async ({ page }) => {
    await page.goto("/admin/configuration/billing");

    await expect(page.getByText("No such configuration area")).toBeVisible();
  });

  test("counts views and publishes with approved analytics events only", async ({ page, request }) => {
    await page.goto("/admin/configuration/models");
    await page.getByRole("button", { name: "Publish configuration" }).click();
    await expect(page.getByRole("alert").filter({ hasText: "A published configuration needs a new version." })).toBeVisible();

    await expect
      .poll(async () => (await request.get(`${mockApi}/__analytics`)).json())
      .toEqual(
        expect.arrayContaining([
          { name: "admin.viewed", properties: { area: "models" } },
          { name: "configuration.published", properties: { area: "models", outcome: "refused" } },
        ]),
      );

    await page.getByRole("navigation", { name: "Control plane" }).getByRole("link", { name: "Overview" }).click();
    const usage = page.getByRole("table", { name: "Product usage" });
    await expect(usage.getByRole("row").filter({ hasText: "configuration.published" })).toContainText("area=models, outcome=refused");
    await expect(usage.getByRole("row").filter({ hasText: "area=models" }).filter({ hasText: "admin.viewed" })).toContainText("1");
  });

  test("tells a non-operator that the control plane is not theirs", async ({ page, request }) => {
    await request.post(`${mockApi}/__fail`, { data: { route: "admin", status: 403 } });
    await page.goto("/admin");
    await expect(page.getByText("The control plane is for operators")).toBeVisible();

    await page.goto("/admin/configuration/models");
    await expect(page.getByText("The control plane is for operators")).toBeVisible();
  });
});

for (const path of ["/", `/incidents/${incidentId}`, `/incidents/${incidentId}/postmortem`, "/admin", "/admin/policy", "/admin/configuration/runtime"]) {
  for (const theme of ["dark", "light"] as const) {
    test(`has no accessibility violations on ${path} in the ${theme} theme`, async ({ page, request }) => {
      await request.post(`${mockApi}/__reset`);
      await page.goto(path);
      await page.evaluate((selected) => {
        document.documentElement.classList.toggle("dark", selected === "dark");
      }, theme);

      const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();

      expect(results.violations).toEqual([]);
    });
  }
}

// The incident page and the control plane at the width the project runs at (Phase E02): 1280 px in the app project and
// 768 px in app-tablet. The live indicator's state depends on timing, so it is masked.
for (const [name, path] of [["incident", `/incidents/${incidentId}`], ["control-plane", "/admin"]] as const) {
  test(`lays out the ${name} page without accessibility violations`, { tag: "@layout" }, async ({ page, request }, testInfo) => {
    await request.post(`${mockApi}/__reset`);
    await page.goto(path);
    await page.evaluate(() => document.fonts.ready);

    const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
    await expect(page).toHaveScreenshot(`${testInfo.project.name}-${name}.png`, { fullPage: true, mask: [page.getByRole("status")] });
  });
}
