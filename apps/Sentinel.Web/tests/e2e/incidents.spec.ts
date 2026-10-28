import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

const wcagTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
const mockApi = "http://127.0.0.1:3199";
const incidentTitle = "checkout-service slo_burn_rate at 14.8";

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
    await page.getByRole("link", { name: incidentTitle }).click();

    await expect(page.getByRole("heading", { level: 1, name: incidentTitle })).toBeVisible();
    await expect(page.getByText("Diagnosed", { exact: true })).toBeVisible();
    await expect(page.getByLabel("Decision checks")).toContainText("before symptom onset");
    await expect(page.getByRole("table", { name: "Evidence" })).toContainText("Redis connections rose from 4 to 186");
    await expect(page.getByText("Roll back checkout-service from v2.4.1 to v2.4.0").first()).toBeVisible();
    await expect(page.getByText("Approval required")).toBeVisible();
    await expect(page.getByLabel("Incident timeline")).toContainText("Plan v1 proposed");
  });

  test("approves the exact plan version after confirmation", async ({ page }) => {
    await page.getByRole("link", { name: incidentTitle }).click();
    await page.getByRole("button", { name: "Approve plan" }).click();

    const dialog = page.getByRole("dialog", { name: "Approve plan v1?" });
    await expect(dialog).toContainText("sha256:6f1c2b9e");
    await dialog.getByRole("button", { name: "Confirm approval" }).click();

    // The page re-renders from Api: the plan is approved and the incident is remediating.
    await expect(page.getByText("Approved by responder@example.test")).toBeVisible();
    await expect(page.getByText("Remediating", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve plan" })).toHaveCount(0);
  });

  test("requires a reason to reject a plan", async ({ page }) => {
    await page.getByRole("link", { name: incidentTitle }).click();
    await page.getByRole("button", { name: "Reject plan" }).click();

    const dialog = page.getByRole("dialog", { name: "Reject plan v1?" });
    await dialog.getByLabel("Reason").fill("Conflicts with a scheduled migration.");
    await dialog.getByRole("button", { name: "Confirm rejection" }).click();

    await expect(page.getByText("Escalated", { exact: true })).toBeVisible();
    await expect(page.getByText("Rejected", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Reject plan" })).toHaveCount(0);
  });

  test("reports an unknown incident without failing the page", async ({ page }) => {
    await page.goto("/incidents/0192f0c4-0000-7000-8000-00000000dead");

    await expect(page.getByRole("status")).toContainText("Incident not found");
  });

  for (const path of ["/", "/incidents/0192f0c4-0000-7000-8000-000000000001"]) {
    for (const theme of ["dark", "light"] as const) {
      test(`has no accessibility violations on ${path} in the ${theme} theme`, async ({ page }) => {
        await page.goto(path);
        await page.evaluate((selected) => {
          document.documentElement.classList.toggle("dark", selected === "dark");
        }, theme);

        const results = await new AxeBuilder({ page }).withTags(wcagTags).analyze();

        expect(results.violations).toEqual([]);
      });
    }
  }
});
