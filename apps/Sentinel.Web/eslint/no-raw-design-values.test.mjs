// The design-value rule against fixtures: what feature code may write and what it may not (Phase E02).
// Run by `npm run lint`.
import { RuleTester } from "eslint";

import plugin from "./no-raw-design-values.mjs";

const tester = new RuleTester({
  languageOptions: { ecmaVersion: "latest", sourceType: "module", parserOptions: { ecmaFeatures: { jsx: true } } },
});

tester.run("no-raw-design-values", plugin.rules["no-raw-design-values"], {
  valid: [
    { code: `<p className="text-body text-text-muted" />` },
    { code: `<p className="text-caption tabular-nums" />` },
    { code: `<div className="shadow-raised data-[state=open]:shadow-overlay" />` },
    { code: `cn("text-label", condition && "text-title")` },
    { code: `<div title="text-lg z-50" />`, name: "only class strings are checked" },
  ],
  invalid: [
    { code: `<p className="text-[13px]" />`, errors: [{ messageId: "arbitraryValue" }] },
    { code: `<p className="text-lg" />`, errors: [{ messageId: "typeSize" }] },
    { code: `<p className="md:text-sm" />`, errors: [{ messageId: "typeSize" }] },
    { code: `<div className="z-[60]" />`, errors: [{ messageId: "arbitraryValue" }] },
    { code: `<div className="z-50" />`, errors: [{ messageId: "zIndex" }] },
    { code: `<div className="shadow-md" />`, errors: [{ messageId: "shadow" }] },
    { code: `cn("bg-red-500")`, errors: [{ messageId: "paletteColor" }] },
  ],
});

console.log("no-raw-design-values: every fixture behaves.");
