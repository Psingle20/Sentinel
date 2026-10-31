import AxeBuilder from "@axe-core/playwright";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { Page } from "@playwright/test";

import { expect, mockApi, test, wcagTags } from "../support/hydrated-test";

// Phase E04, Judge mode: each captured story is a recorded run of the real system, trimmed to what the Gateway serves.
type Capture = { runRecord: { incident: { summary: { incidentId: string } } } };

function captured(scenario: string): string {
  const capture = JSON.parse(readFileSync(join(process.cwd(), "tests", "support", "fixtures", "captures", `${scenario}.json`), "utf8")) as Capture;
  return capture.runRecord.incident.summary.incidentId;
}

const liveIncidentId = "0192f0c4-0000-7000-8000-000000000001";

function rail(page: Page) {
  return page.getByRole("navigation", { name: "Stages" });
}

async function openStage(page: Page, title: string) {
  await rail(page).getByRole("button", { name: new RegExp(`^${title}`) }).click();
  await expect(page.locator("#stage-title")).toContainText(title);
}

function summary(page: Page) {
  return page.getByLabel("Summary");
}

test.describe("judge mode", () => {
  test.beforeEach(async ({ request }) => {
    await request.post(`${mockApi}/__reset`);
  });

  test("tells a captured S9 run from context to proven recovery, with the planted instruction shown and ignored", { tag: "@layout" }, async ({ page }) => {
    const incidentId = captured("S9");
    await page.goto(`/incidents/${incidentId}/judge?source=captured`);

    const truth = page.getByRole("region", { name: "Truth bar" });
    await expect(truth).toContainText("Captured");
    await expect(truth).not.toContainText("Live");
    await expect(rail(page).getByRole("button", { name: /^Context/ })).toHaveAttribute("aria-current", "step");

    await openStage(page, "Manipulation");
    await expect(summary(page)).toContainText("addressed to AI agents");
    await expect(summary(page)).toContainText("did not act on them");

    await openStage(page, "Recovery");
    await expect(summary(page)).toContainText("Independent telemetry proved recovery");
    await expect(page.getByRole("region", { name: "Outcome" })).toContainText("Resolved");

    // Every source and the workspace link stay inside the incident's own workspace.
    for (const link of await page.getByRole("region", { name: "Sources" }).getByRole("link").all()) {
      await expect(link).toHaveAttribute("href", new RegExp(`^/incidents/${incidentId}`));
    }

    const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test("sets the deploy-blame baseline's action beside Sentinel's verdict in the captured S1 run", async ({ page }) => {
    await page.goto(`/incidents/${captured("S1")}/judge?source=captured`);

    await openStage(page, "Counterfactual");
    await expect(summary(page)).toContainText("would have rolled back checkout-service v2.4.1");
    await openStage(page, "Governance");
    await expect(summary(page)).toContainText("approved it");
  });

  test("shows S10's first remedy, its unrecovered verdict and the follow-up cycle without hiding the correction", async ({ page }) => {
    await page.goto(`/incidents/${captured("S10")}/judge?source=captured`);

    await openStage(page, "Self-correction");
    await expect(summary(page)).toContainText("took effect at its target, but recovery was");
    await expect(page.getByRole("region", { name: "Sources" })).toContainText("Follow-up cycle:");

    await openStage(page, "Action");
    await expect(summary(page)).toContainText("order-service");
    await openStage(page, "Recovery");
    await expect(rail(page).getByRole("button", { name: /^Recovery/ })).toContainText("Complete");
  });

  test("plays a captured run at accelerated speed and keeps the recorded times", async ({ page }) => {
    await page.goto(`/incidents/${captured("S9")}/judge?source=captured`);
    const recorded = await page.locator("#stage-title").locator("xpath=ancestor::*[@role='region']").locator("time").getAttribute("datetime");

    await page.getByRole("button", { name: "Play" }).click();
    await expect(page.getByRole("region", { name: "Truth bar" })).toContainText("every time shown is the recorded one");
    await expect(rail(page).getByRole("button", { name: /^Detection/ })).toHaveAttribute("aria-current", "step", { timeout: 10_000 });
    await page.getByRole("button", { name: "Pause" }).click();

    await openStage(page, "Context");
    await expect(page.locator("#stage-title").locator("xpath=ancestor::*[@role='region']").locator("time")).toHaveAttribute("datetime", recorded ?? "");
  });

  test("moves only when asked under reduced motion", async ({ page }) => {
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.goto(`/incidents/${captured("S9")}/judge?source=captured`);

    await page.waitForTimeout(4_000);
    await expect(rail(page).getByRole("button", { name: /^Context/ })).toHaveAttribute("aria-current", "step");
    await page.getByRole("button", { name: "Next" }).click();
    await expect(rail(page).getByRole("button", { name: /^Detection/ })).toHaveAttribute("aria-current", "step");
  });

  test("follows a live incident to its newest stage and says what the next human step is", { tag: "@layout" }, async ({ page }) => {
    await page.goto(`/incidents/${liveIncidentId}/judge`);

    await expect(page.getByRole("region", { name: "Truth bar" })).toContainText("Live");
    await expect(rail(page).getByRole("button", { name: /^Governance/ })).toHaveAttribute("aria-current", "step");
    await expect(rail(page).getByRole("button", { name: /^Governance/ })).toContainText("In progress");
    await expect(page.getByRole("region", { name: "Outcome" })).toContainText("An approver reviews plan v1 in the workspace.");

    await openStage(page, "Decision");
    await page.getByRole("button", { name: "Follow newest" }).click();
    await expect(rail(page).getByRole("button", { name: /^Governance/ })).toHaveAttribute("aria-current", "step");

    const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();
    expect(results.violations).toEqual([]);
  });

  test("marks model detail unavailable when AgentWorker is down instead of inferring it", async ({ page, request }) => {
    await request.post(`${mockApi}/__fail`, { data: { route: "agent-executions", status: 503 } });
    await page.goto(`/incidents/${liveIncidentId}/judge`);

    await expect(page.getByRole("region", { name: "Truth bar" })).toContainText("Agent-execution detail unavailable");
    await openStage(page, "Investigation");
    await expect(summary(page)).toContainText("Agent-execution detail is unavailable");
  });

  test("refuses to play a run record from an unknown schema version", async ({ page, request }) => {
    await request.post(`${mockApi}/__runrecord`, { data: { schemaVersion: 99 } });
    await page.goto(`/incidents/${liveIncidentId}/judge?source=captured`);

    await expect(page.getByText("This run record cannot be played")).toBeVisible();
    await expect(page.getByText("schema version 99")).toBeVisible();
  });

  test("is reachable from the incident page", async ({ page }) => {
    await page.goto(`/incidents/${liveIncidentId}`);
    await page.getByRole("link", { name: "Judge mode" }).click();

    await expect(page).toHaveURL(new RegExp(`/incidents/${liveIncidentId}/judge$`));
    await expect(page.getByRole("navigation", { name: "Stages" })).toBeVisible();
  });
});
