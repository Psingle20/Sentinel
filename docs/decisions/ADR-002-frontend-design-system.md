# ADR-002: Framework-first frontend design system

- Status: Accepted
- Date: 2026-10-25

## Context

Sentinel's frontend will be developed by multiple human and AI contributors. Utility classes without a governed component and token system would permit visually valid but inconsistent screens. Hand-written CSS would increase divergence, accessibility risk and review cost.

The incident-response interface also communicates safety-critical distinctions such as supporting versus contradicting evidence, approval requirements, action progress and recovery failure.

## Constraints

- The frontend is `apps/Sentinel.Web` and communicates only through `Sentinel.Api`.
- The design system must support dense operational views, evidence graphs, live timelines and dark mode.
- Accessibility and keyboard operation are required.
- Contributors need a discoverable, testable catalogue of approved components and states.
- A second frontend consumer does not yet exist, so a shared package would be premature.

## Considered options

1. Hand-written CSS and custom components.
2. Tailwind CSS without a governed component system.
3. A monolithic component framework such as Material UI or Ant Design.
4. Tailwind CSS with shadcn/ui recipes, Radix UI primitives, semantic tokens and controlled variants.

## Decision

Use Next.js, React and TypeScript with:

- Tailwind CSS as the primary styling framework;
- shadcn/ui as the default component recipe source;
- Radix UI for accessible interaction primitives;
- CVA for controlled variants;
- CSS variables for foundation and semantic design tokens;
- Storybook for the component catalogue;
- Playwright for critical-flow and visual-regression tests.

The design system initially lives inside `apps/Sentinel.Web/src/design-system`. It may be extracted only when a second named consumer exists.

## Enforcement

- Prefer approved components over raw element composition.
- Prohibit arbitrary raw colors, spacing, typography, shadows and radii in feature code.
- Avoid inline styles, CSS-in-JS and page-specific CSS for ordinary UI.
- Permit custom CSS only for documented visualization, motion, print/export or third-party integration requirements.
- Require component stories, state coverage and accessibility checks.
- Require another ADR before adding a competing styling or component framework.

## Consequences

### Positive

- Human and AI contributors share one visual and interaction vocabulary.
- Accessibility behavior comes from established primitives rather than repeated custom controls.
- Visual changes are reviewable through tokens, stories and regression tests.
- Sentinel retains ownership of component source and can evolve its operational language.

### Negative

- Phase 00 includes design-system and testing setup before feature screens.
- shadcn components require ownership and maintenance rather than passive package upgrades.
- Strict token and variant rules add review work for genuinely novel visualizations.

## Follow-up work

- Implement the [frontend design-system plan](../architecture/frontend-design-system.md) during Phase 00.
- Record approved dependency versions when the frontend is scaffolded.
- Add contributor guidance that points AI agents to the component catalogue and styling rules.
- Establish the first vertical-slice components before product-screen expansion.

## Revisit criteria

Revisit if accessibility requirements cannot be met, performance measurements identify material framework overhead, or a second frontend requires extraction into `packages/Sentinel.DesignSystem`.

