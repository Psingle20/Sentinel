import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import comments from "@eslint-community/eslint-plugin-eslint-comments/configs";
import storybook from "eslint-plugin-storybook";
import sentinelDesignSystem from "./eslint/no-raw-design-values.mjs";

// Competing component, CSS or CSS-in-JS systems require an ADR (ADR-002).
const unapprovedUiLibraries = [
  "@mui/*",
  "@material-ui/*",
  "antd",
  "@ant-design/*",
  "@chakra-ui/*",
  "bootstrap",
  "react-bootstrap",
  "@mantine/*",
  "semantic-ui-react",
  "@headlessui/*",
  "react-aria-components",
  "@base-ui-components/*",
  "styled-components",
  "@emotion/*",
  "@stitches/*",
];

const eslintConfig = defineConfig([
  ...nextVitals,
  ...nextTs,
  comments.recommended,
  ...storybook.configs["flat/recommended"],
  {
    rules: {
      // Every exception to a lint rule must say why.
      "@eslint-community/eslint-comments/require-description": "error",
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            {
              group: unapprovedUiLibraries,
              message: "Not part of the approved UI stack. Adding it requires an ADR (ADR-002).",
            },
          ],
        },
      ],
    },
  },
  {
    // Feature code and stories compose the design system; only the design system owns raw values and styles.
    files: ["src/app/**/*.{ts,tsx}", "src/features/**/*.{ts,tsx}", "stories/**/*.{ts,tsx}"],
    plugins: { sentinel: sentinelDesignSystem },
    rules: {
      "sentinel/no-raw-design-values": "error",
      "react/forbid-dom-props": ["error", { forbid: ["style"] }],
      "react/forbid-component-props": ["error", { forbid: ["style"] }],
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
    "storybook-static/**",
    "playwright-report/**",
    "test-results/**",
  ]),
]);

export default eslintConfig;
