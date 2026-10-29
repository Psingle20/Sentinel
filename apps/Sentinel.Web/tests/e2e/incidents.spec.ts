import AxeBuilder from "@axe-core/playwright";
import { expect, type Page, test } from "@playwright/test";

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

  test("shows the decision, evidence and plan for an incident", async ({ page }) => {
    await openIncident(page);

    await expect(page.getByText("Diagnosed", { exact: true })).toBeVisible();
    await expect(page.getByLabel("Decision checks")).toContainText("before symptom onset");
    await expect(page.getByRole("table", { name: "Evidence" })).toContainText("Redis connections rose from 4 to 186");
    await expect(page.getByText("Roll back checkout-service from v2.4.1 to v2.4.0").first()).toBeVisible();
    await expect(page.getByText("Approval required").first()).toBeVisible();
    await expect(page.getByLabel("Incident timeline")).toContainText("Plan v1 proposed");
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

  test("reports an unknown incident without failing the page", async ({ page }) => {
    await page.goto("/incidents/0192f0c4-0000-7000-8000-00000000dead");

    await expect(page.getByRole("status")).toContainText("Incident not found");
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

  test("is reachable from the incident list", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("link", { name: "Control plane" }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Control plane" })).toBeVisible();
    await expect(page.getByText("Remediation policy policy-2026.10.2")).toBeVisible();
    await expect(page.getByRole("list", { name: "Provider sources" })).toContainText("loki-reference");
  });

  test("engages the emergency stop and audits it", async ({ page }) => {
    await page.goto("/admin");
    await page.getByRole("button", { name: "Engage emergency stop" }).click();
    await page.getByRole("dialog", { name: "Engage the emergency stop?" }).getByRole("button", { name: "Engage", exact: true }).click();

    await expect(page.getByText("Emergency stop engaged", { exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("emergency-stop.engaged");
    await expect(page.getByRole("button", { name: "Release emergency stop" })).toBeVisible();
  });

  test("publishes a new policy version", async ({ page }) => {
    await page.goto("/admin");
    const editor = page.getByRole("textbox", { name: "Policy document (give it a new version)" });
    const document = await editor.inputValue();
    await editor.fill(document.replace('"policy-2026.10.2"', '"policy-2026.10.3"'));
    await page.getByRole("button", { name: "Publish policy" }).click();

    await expect(page.getByText("Published policy policy-2026.10.3.")).toBeVisible();
    await expect(page.getByRole("list", { name: "Policy versions" })).toContainText("policy-2026.10.3");
    await expect(page.getByRole("table", { name: "Audit history" })).toContainText("policy.published");
  });

  test("refuses a policy without a new version", async ({ page }) => {
    await page.goto("/admin");
    await page.getByRole("button", { name: "Publish policy" }).click();

    await expect(page.getByRole("alert").filter({ hasText: "A published policy needs a new version." })).toBeVisible();
  });

  test("tells a non-operator that the control plane is not theirs", async ({ page, request }) => {
    await request.post(`${mockApi}/__fail`, { data: { route: "admin", status: 403 } });
    await page.goto("/admin");

    await expect(page.getByText("The control plane is for operators")).toBeVisible();
  });
});

for (const path of ["/", `/incidents/${incidentId}`, `/incidents/${incidentId}/postmortem`, "/admin"]) {
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
