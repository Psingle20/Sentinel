import { defineConfig, devices } from "@playwright/test";

const appUrl = "http://127.0.0.1:3100";
const storybookUrl = "http://127.0.0.1:6106";
// The web app's own tests run against a fixture-backed stand-in for Sentinel.Api.
const mockApiUrl = "http://127.0.0.1:3199";
const isCi = Boolean(process.env.CI);

export default defineConfig({
  testDir: "./tests",
  forbidOnly: isCi,
  retries: isCi ? 1 : 0,
  reporter: isCi ? "github" : "list",
  // Baselines are per platform: font rendering differs between Windows, macOS and Linux.
  snapshotPathTemplate: "{testDir}/__screenshots__/{platform}/{arg}{ext}",
  expect: {
    toHaveScreenshot: { animations: "disabled", maxDiffPixelRatio: 0.002 },
  },
  use: {
    ...devices["Desktop Chrome"],
    viewport: { width: 1280, height: 800 },
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "app",
      testMatch: "e2e/**/*.spec.ts",
      use: { baseURL: appUrl },
    },
    {
      name: "storybook",
      testMatch: "visual/**/*.spec.ts",
      use: { baseURL: storybookUrl },
    },
  ],
  webServer: [
    {
      command: "node tests/support/mock-api.mjs 3199",
      url: `${mockApiUrl}/api/v1/incidents`,
      reuseExistingServer: !isCi,
      timeout: 30_000,
    },
    {
      command: "npm run build && npm run start -- --hostname 127.0.0.1 --port 3100",
      url: appUrl,
      reuseExistingServer: !isCi,
      timeout: 300_000,
      env: { SENTINEL_API_URL: mockApiUrl },
    },
    {
      // The npm test scripts build Storybook first: the visual spec reads its index while collecting tests.
      command: "node scripts/serve-static.mjs storybook-static 6106",
      url: `${storybookUrl}/index.json`,
      reuseExistingServer: !isCi,
      timeout: 300_000,
    },
  ],
});
