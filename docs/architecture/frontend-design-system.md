# Sentinel frontend design system

## Purpose

Sentinel's interface is an operational surface where responders must distinguish evidence, uncertainty, risk, approval state and recovery progress quickly. A consistent design language is therefore a product and safety requirement, not cosmetic polish.

Multiple human and AI contributors will build the frontend. They must compose an established system rather than inventing styles per screen.

## Approved stack

```text
Next.js + React + TypeScript
        |
        v
Tailwind CSS
        |
        v
shadcn/ui recipes + Radix UI primitives
        |
        v
Sentinel semantic tokens, variants and product patterns
```

- **Tailwind CSS** is the primary styling framework and token-consumption mechanism.
- **shadcn/ui** supplies the default component recipes that Sentinel owns and can adapt.
- **Radix UI** supplies accessible behavior for dialogs, menus, popovers, tabs, tooltips and similar interaction primitives.
- **Class Variance Authority (CVA)** defines controlled component variants.
- **Storybook** is the component catalogue and interaction review surface.
- **Playwright** provides critical-flow and visual-regression coverage.

Do not add Bootstrap, Material UI, Chakra UI, Ant Design or another competing component/CSS system without an ADR.

## Styling rules

1. Use an approved design-system component before creating a new component.
2. Use semantic tokens instead of raw color, spacing, radius, shadow, typography or z-index values.
3. Use Tailwind utilities and controlled variants for normal styling.
4. Do not use inline styles, CSS-in-JS or page-specific style sheets for ordinary product UI.
5. Custom CSS is an explicit exception for visualization canvases, complex motion, print/export behavior or third-party integration fixes.
6. Repeated compositions become named product patterns rather than copied class lists.
7. Dark mode is the primary operational theme; light mode must remain accessible and complete.
8. Motion must respect reduced-motion preferences and must never obscure incident state.

## Token model

Foundation tokens describe the visual system:

- color palette;
- typography scale;
- spacing and sizing scale;
- radii, borders and elevation;
- motion duration and easing;
- breakpoints and layout constraints.

Semantic tokens communicate operational meaning:

```text
surface-canvas
surface-panel
text-primary
text-muted
border-subtle
incident-critical
incident-warning
incident-stable
evidence-supporting
evidence-contradicting
evidence-neutral
hypothesis-leading
approval-required
action-executing
recovery-improving
recovery-failed
```

Components consume semantic tokens. Application screens do not depend directly on palette values.

## Component layers

```text
Primitives
  Button, Input, Dialog, Tabs, Tooltip, Table, Badge

Domain components
  IncidentStatusBadge, SeverityIndicator, ConfidenceDisplay,
  EvidenceDirectionBadge, ApprovalState, RecoveryState

Product patterns
  IncidentTimeline, EvidenceGraph, HypothesisComparison,
  RemediationReview, AgentActivity, RecoveryPanel

Screens
  Compose product patterns; do not restyle primitives independently
```

The initial implementation stays inside `apps/Sentinel.Web/src/design-system`. It becomes a reusable package only when a second real frontend consumer exists.

## Multi-agent contribution contract

Every contributor, including AI agents, must:

- inspect the component catalogue and tokens before implementing a screen;
- reuse approved components and variants;
- add a Storybook story for every new component or meaningful state;
- cover loading, empty, error, stale, degraded and permission-denied states;
- validate keyboard access, focus behavior, contrast and reduced motion;
- add or update visual-regression baselines intentionally;
- document why custom CSS or a new dependency is necessary;
- avoid changing global tokens to solve a single-screen problem.

Review rejects arbitrary values, duplicated component patterns, competing UI libraries and inaccessible custom controls.

## Structure

```text
apps/Sentinel.Web/
├── src/
│   ├── app/
│   ├── design-system/
│   │   ├── primitives/
│   │   ├── domain/
│   │   ├── patterns/
│   │   ├── tokens/
│   │   └── themes/
│   ├── features/
│   └── lib/
├── stories/
└── tests/
```

The [Sentinel.Web README](../../apps/Sentinel.Web/README.md) lists the commands and the checks that enforce these rules: the removed default palette, the lint rule against raw values and inline styles, the dependency and stylesheet checks, and axe plus screenshot tests for every story in both themes.

## Delivery sequence

### Phase 00 foundation

Implemented on 2026-10-28, except running the checks in CI, which waits for the CI pipeline.

- Configure Next.js, TypeScript, Tailwind CSS, shadcn/ui and Radix UI.
- Define foundation and semantic tokens as CSS variables consumed through Tailwind.
- Establish CVA variant conventions.
- Configure Storybook, accessibility checks and visual-regression testing.
- Implement only the primitives and incident-shell patterns required by the first vertical slice.
- Add linting or review checks for raw values, inline styles and unauthorized UI dependencies.

### Vertical-slice growth

Add components only when the golden path needs them. Each new state is added to the catalogue before or with its feature implementation.

### Phase 08 product depth

- Complete domain components and product patterns for all responder and administrator journeys.
- Validate responsive layouts, large evidence sets and reconnect/degraded states.
- Complete accessibility, theme and visual-regression coverage.
- Use real workflow states and contracts rather than mock-only presentation states.

## Acceptance criteria

- The golden path uses approved components and semantic tokens end to end.
- No ordinary screen introduces inline styling, raw palette values or an independent styling system.
- Every reusable component exposes documented states and controlled variants.
- Storybook demonstrates operational, error and permission states.
- Automated accessibility and visual-regression checks run in CI.
- A contributor can build a new screen without inventing typography, color, spacing or interaction behavior.

