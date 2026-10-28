import { execFileSync } from "node:child_process";
import { pathToFileURL } from "node:url";
import fs from "node:fs";
import path from "node:path";

const projectRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/(.:)/, "$1")), "..");
const source = path.join(projectRoot, "docs", "architecture", "assets", "sentinel-product-architecture.svg");
const output = path.join(projectRoot, "docs", "architecture", "assets", "sentinel-product-architecture.png");
const browserCandidates = [
  process.env.CHROMIUM_EXECUTABLE,
  "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
].filter(Boolean);
const executablePath = browserCandidates.find((candidate) => fs.existsSync(candidate));

if (!executablePath) {
  throw new Error("Set CHROMIUM_EXECUTABLE to an installed Chrome or Edge executable.");
}

execFileSync(executablePath, [
  "--headless=new",
  "--disable-gpu",
  "--hide-scrollbars",
  "--window-size=3000,1600",
  `--screenshot=${output}`,
  pathToFileURL(source).href,
], { stdio: "inherit" });

console.log(`Rendered ${output}`);
