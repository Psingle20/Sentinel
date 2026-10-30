# Phase E02 – Frontend design-system depth

Enhancement phase. Design source: [Sentinel frontend design system](../architecture/frontend-design-system.md) and its [gap assessment of 2026-10-30](../architecture/frontend-design-system.md#gap-assessment-2026-10-30), under [ADR-002](../decisions/ADR-002-frontend-design-system.md). This document is the delivery view: what is built, in which order, how it is tested and when it is done. The design-system plan keeps the stack, the rules and the token model; nothing here changes them.

## Outcome

Feature code in `apps/Sentinel.Web` composes the design system for every form, message and layout it renders, with no hand-written label, result or control blocks left; the token model the plan promises exists in full (typography, spacing, elevation, z-index and motion as well as colour) and the lint rule can therefore cover it; a responder can choose the light theme and use the product on a narrow screen; and the catalogue keeps proving every state in both themes with accessibility and screenshot checks. The stack and the governance rules stay as ADR-002 decided them.

## Primary service ownership

`Sentinel.Web` owns all of this phase. `Sentinel.Gateway` and the backend services change nothing; the phase adds no API, contract or message. The design system stays inside `apps/Sentinel.Web/src/design-system` because there is still one frontend consumer.

## Entry evidence required

- The Phase 00 design-system foundation is implemented (2026-10-28): tokens, themes, the lint rule, the stylesheet and dependency checks, Storybook with axe and screenshot coverage ([Sentinel.Web README](../../apps/Sentinel.Web/README.md)).
- The responder and administrator journeys exist end to end against the mock API, so every new primitive has a real caller ([incidents.spec.ts](../../apps/Sentinel.Web/tests/e2e/incidents.spec.ts)).
- The gap assessment names the gaps this phase closes; nothing here is speculative.

## Workstreams

The assessment's tiers are the workstreams. Tier 1 has no dependency and removes the drift that exists today; Tier 2 makes two styling rules true in the running app; Tier 3 is hygiene and the open acceptance criterion.

| Tier | Work | Evidence when done |
| --- | --- | --- |
| 1 – Form and feedback primitives | `Label`, `Input`, `Select`, `Checkbox` and a `Field` composition (label, control, description, error) under `primitives/`; an `ActionResult` pattern that renders a server action's outcome with `role="status"` or `role="alert"`; `plan-decision.tsx`, `narrative-editor.tsx`, `replay-panel.tsx` and the admin `controls.tsx` rewritten to use them, so no feature file writes a `<label>` or a result paragraph by hand | Stories for every state; the grep for `<label` in `src/features` and `src/app` returns nothing; end-to-end suite unchanged and green |
| 1 – Token model completed | Typography tokens (display, title, body, label, caption, code) and a spacing scale exposed through `theme.css`; elevation and z-index tokens; tabular numerals on every numeric column and figure; the lint rule extended to reject raw `text-*` sizes and `z-*` values in feature code once the tokens exist | Foundations stories for typography, spacing and elevation; lint passes with the extended rule |
| 1 – Motion and vocabulary | `--duration-*` and `--ease-standard` wired into dialog, tooltip, skeleton and `tw-animate-css` presets so reduced motion is honoured through the tokens; `Badge` loses its `destructive` variant so `StatusBadge` is the only status vocabulary; the shadcn recipe tokens feature code may use are listed in the plan, and the unused chart and sidebar tokens are removed or marked reserved | Primitives reference the motion tokens; one status vocabulary; the plan's token section lists the allowed recipe tokens |
| 2 – Theme selection | A theme choice honouring `prefers-color-scheme`, persisted per browser, set before first paint without a flash, with a control in the app shell; the end-to-end suite runs the incident journey once in light | Light theme reachable in the app and tested |
| 2 – Responsive layout | A tablet viewport (768 px) in Playwright for the incident page, the control plane and the shell; the primary navigation collapses on narrow screens; dense tables scroll or stack by rule, documented in the plan | Tablet screenshots and an end-to-end run at 768 px pass |
| 3 – Catalogue and CI readiness | Interaction stories with `play` functions for the approval, rejection and revocation dialogs and the editors; `cn` taken from the local utility the shadcn config names; no hard-coded locale in components; the evidence graph recorded as the visualization exception the rules allow; Linux screenshot baselines generated when the CI pipeline exists | Interaction stories pass; baselines for a second platform |

## In scope

- Everything the table lists, inside `apps/Sentinel.Web`.
- The design-system plan's token and component-layer sections updated as the pieces land, so the plan keeps describing what exists.
- Storybook stories and screenshot baselines for every new component, state and theme.

## Primary deliverables

1. The form and feedback primitives and the `Field` and `ActionResult` compositions, with feature code migrated onto them.
2. The completed token model and the extended lint rule.
3. Motion tokens in use and one status vocabulary.
4. Theme selection and a light-theme end-to-end run.
5. Tablet coverage and the responsive rules.
6. Interaction stories and the hygiene fixes.

## Test plan

- Every new primitive, pattern and state has a story; the visual suite runs axe at WCAG 2.2 AA and a screenshot in both themes for each, as it does today.
- `npm run lint` with the extended rule passes on feature code and fails on a deliberately raw `text-[13px]`, `text-lg` or `z-[60]` in a test fixture.
- The end-to-end suite keeps passing after each migration; the incident journey runs once in light and once at the tablet viewport.
- Interaction stories exercise open, validate, submit and cancel for the dialogs and editors.
- Keyboard-only runs of the approval and configuration flows, recorded in the stories' `play` functions.

## Exit criteria

- No feature file under `src/features` or `src/app` writes a label, input, select or result message by hand; each composes a primitive or pattern.
- Typography, spacing, elevation, z-index and motion tokens exist, are documented in the plan, and the lint rule covers them in feature code.
- Light and dark are both selectable in the app and both pass the end-to-end incident journey.
- The incident page and the control plane pass accessibility and screenshot checks at 1280 px and 768 px.
- `StatusBadge` is the only status vocabulary and the plan lists the recipe tokens feature code may use.
- Every story still passes axe and screenshot checks in both themes, and the catalogue includes interaction stories for the dialogs and editors.

## Deferred deliberately

- Extracting the design system into a package; ADR-002 allows that only when a second frontend exists.
- Any new styling or component framework, which needs an ADR.
- A visual redesign of the product; this phase completes the system, it does not restyle it.
- A command palette, keyboard-shortcut scheme or notification centre; each is a product feature for [Phase 08](phase-08-product-experience.md) or a later enhancement, not a design-system gap.
- Mobile-phone layouts below 768 px; the responder surface is a desktop and tablet product until a journey says otherwise.
