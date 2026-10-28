// Checks the design-system rules that ESLint cannot see:
//   - stylesheets live only in src/design-system/ or the root globals.css;
//   - package.json does not depend on a competing UI, CSS or CSS-in-JS system.
// Exceptions must be listed in design-system-exceptions.json with a reason.

import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative, sep } from "node:path";

const root = new URL("..", import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1");
const srcDir = join(root, "src");

const forbiddenDependencies = [
  /^@mui\//,
  /^@material-ui\//,
  /^antd$/,
  /^@ant-design\//,
  /^@chakra-ui\//,
  /^bootstrap$/,
  /^react-bootstrap$/,
  /^@mantine\//,
  /^semantic-ui-react$/,
  /^@headlessui\//,
  /^react-aria-components$/,
  /^@base-ui-components\//,
  /^styled-components$/,
  /^@emotion\//,
  /^@stitches\//,
];

const exceptionsFile = join(root, "design-system-exceptions.json");
const exceptions = existsSync(exceptionsFile) ? JSON.parse(readFileSync(exceptionsFile, "utf8")) : { stylesheets: [] };
const allowedStylesheets = new Set(exceptions.stylesheets.map((entry) => entry.path));

function* walk(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) yield* walk(path);
    else yield path;
  }
}

const problems = [];

for (const file of walk(srcDir)) {
  if (!/\.(css|scss|sass|less)$/.test(file)) continue;
  const path = relative(root, file).split(sep).join("/");
  const allowed =
    path.startsWith("src/design-system/") || path === "src/app/globals.css" || allowedStylesheets.has(path);
  if (!allowed) {
    problems.push(`${path}: stylesheets belong in src/design-system/ unless listed in design-system-exceptions.json.`);
  }
}

const manifest = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
for (const name of Object.keys({ ...manifest.dependencies, ...manifest.devDependencies })) {
  if (forbiddenDependencies.some((pattern) => pattern.test(name))) {
    problems.push(`package.json: '${name}' is not part of the approved UI stack. Adding it requires an ADR (ADR-002).`);
  }
}

if (problems.length > 0) {
  console.error(problems.join("\n"));
  process.exit(1);
}

console.log("Design-system checks passed.");
