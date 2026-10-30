import { readFileSync } from "node:fs";
import { join } from "node:path";

import AxeBuilder from "@axe-core/playwright";
import { expect, type Page, test } from "@playwright/test";

type IndexEntry = { id: string; type: string; title: string; name: string };

// The story list comes from the Storybook build, so every new story is covered automatically.
const index = JSON.parse(readFileSync(join(process.cwd(), "storybook-static", "index.json"), "utf8")) as {
  entries: Record<string, IndexEntry>;
};
const stories = Object.values(index.entries).filter((entry) => entry.type === "story");

const wcagTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

// Page-level landmark rules do not apply to a component rendered in isolation.
const pageLevelRules = ["region", "landmark-one-main", "page-has-heading-one"];

// Storybook's a11y addon runs axe in the preview too; when both overlap, axe refuses the second
// run. Waiting and retrying keeps the check strict without depending on which run starts first.
async function analyze(page: Page) {
  for (let attempt = 1; ; attempt++) {
    try {
      return await new AxeBuilder({ page }).withTags(wcagTags).disableRules(pageLevelRules).analyze();
    } catch (error) {
      if (attempt >= 5 || !(error instanceof Error) || !error.message.includes("Axe is already running")) {
        throw error;
      }
      await page.waitForTimeout(250 * attempt);
    }
  }
}

for (const story of stories) {
  for (const theme of ["dark", "light"] as const) {
    test(`${story.title} / ${story.name} (${theme})`, async ({ page }) => {
      await page.goto(`/iframe.html?id=${story.id}&viewMode=story&globals=theme:${theme}`);
      await page.locator("#storybook-root").waitFor();
      await page.evaluate(() => document.fonts.ready);
      await expect(page.locator("html")).toHaveClass(theme === "dark" ? /\bdark\b/ : /^(?!.*\bdark\b)/);

      const results = await analyze(page);
      expect(results.violations, "accessibility violations").toEqual([]);

      await expect(page).toHaveScreenshot(`${story.id}--${theme}.png`, { fullPage: true });

      // An interaction story's play function has finished once the screenshot is stable; one that failed shows
      // Storybook's error display instead of the story.
      await expect(page.locator("body"), "the story rendered and its play function passed").not.toHaveClass(/sb-show-errordisplay/);
    });
  }
}
