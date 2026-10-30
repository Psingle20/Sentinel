# Sentinel.Web

The responder and administrator frontend. It talks only to `Sentinel.Api` and follows the governed [frontend design system](../../docs/architecture/frontend-design-system.md) ([ADR-002](../../docs/decisions/ADR-002-frontend-design-system.md)).

## Stack

Next.js 16 (App Router, Cache Components), React 19, TypeScript, Tailwind CSS 4, shadcn/ui recipes on Radix UI primitives, CVA variants, Storybook 10 and Playwright.

## Layout

```text
src/
├── app/                 routes; compose patterns, never restyle primitives
├── design-system/
│   ├── tokens/          foundation scales and the Tailwind mapping (theme.css)
│   ├── themes/          dark (primary) and light semantic token values
│   ├── primitives/      shadcn/ui recipes owned by Sentinel
│   ├── domain/          status badges, severity, evidence direction, approval, recovery, confidence
│   └── patterns/        app shell, page header, data states
├── features/            feature modules, added with the walking skeleton
└── lib/
stories/                 Storybook catalogue, one file per component family
tests/
├── e2e/                 Playwright tests of the running app
├── visual/              accessibility and screenshot checks for every story
└── __screenshots__/     visual baselines, per platform
eslint/                  the local design-system lint rule
scripts/                 design-system checks and the static server for visual tests
```

## Commands

Run from this directory with Node.js 24 LTS (`.nvmrc`). Install dependencies once with `npm ci`.

| Task | Command |
| --- | --- |
| Develop | `npm run dev`, or start everything with the Aspire AppHost |
| Build | `npm run build` |
| Typecheck | `npm run typecheck` |
| Lint | `npm run lint` |
| Component catalogue | `npm run storybook` |
| End-to-end tests | `npm run test:e2e` |
| Accessibility and visual tests | `npm run test:visual` |
| Update visual baselines | `npm run test:visual:update` |

The Playwright web servers build the app and serve the built Storybook themselves.

## Design-system rules enforced in code

The system's open gaps and the order they are closed in are [Phase E02](../../docs/implementation/phase-e02-frontend-design-system-depth.md).

- Tailwind's default palette is removed: classes such as `bg-red-500` do not exist. Colors come from semantic tokens such as `bg-surface-panel`, `text-incident-critical` or `bg-evidence-supporting-subtle`.
- `npm run lint` rejects, in `src/app`, `src/features` and `stories`: arbitrary values such as `w-[37px]`, raw palette classes, and `style` props. Bracketed state variants such as `data-[state=open]:` are allowed.
- Imports of competing UI, CSS or CSS-in-JS libraries fail lint and the dependency check. Adding one requires an ADR.
- Stylesheets may live only in `src/design-system/` and `src/app/globals.css`; anything else must be listed with a reason in `design-system-exceptions.json`.
- Every lint suppression needs a written reason (`-- why`).
- Every story is checked with axe (WCAG 2.2 AA) and a screenshot in both themes, so a new story is covered automatically.

## Adding a component

1. Check the catalogue (`npm run storybook`) for an existing component or variant.
2. Add shadcn recipes with `npx shadcn@latest add <name>`; they land in `src/design-system/primitives`.
3. Express states as CVA variants backed by semantic tokens, never as raw values.
4. Add a story for every state, including loading, empty, error, stale, degraded and permission-denied where they apply.
5. Run `npm run test:visual:update` and review the new baselines before committing them.

Baselines are platform-specific because font rendering differs. Regenerate the Linux set in CI's environment when CI is added.
