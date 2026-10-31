import { test as base } from "@playwright/test";

// Every page a journey opens is used only once React has hydrated it: a form filled before hydration races the wiring of
// its server action.
export const test = base.extend({
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

export { expect } from "@playwright/test";

export const wcagTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

export const mockApi = "http://127.0.0.1:3199";
