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

### Tokens as implemented

Phase E02 completed the model in [`theme.css`](../../apps/Sentinel.Web/src/design-system/tokens/theme.css) and [`foundation.css`](../../apps/Sentinel.Web/src/design-system/tokens/foundation.css). Feature code, under `src/app` and `src/features`, and stories use these; the lint rule rejects Tailwind's own type sizes, z-index values and shadows there, as it rejects arbitrary values and palette colours. The design system itself may still use Tailwind's scales inside its primitives.

| Kind | Tokens | Use |
| --- | --- | --- |
| Typography | `text-display`, `text-title`, `text-body`, `text-label`, `text-caption`, `text-code` | A role each, with its line height and, for display, title and label, its weight; `text-code` goes with `font-mono` |
| Figures | `tabular-nums` | Every number that is compared or aligned: table cells, confidence, versions, durations, token counts |
| Spacing | Tailwind's scale on `--spacing: 0.25rem` | Every gap, padding and margin is a multiple of 4 px |
| Elevation | `shadow-raised`, `shadow-overlay` | A raised surface on its canvas; an overlay such as a dialog or menu |
| Stacking | `z-overlay`, `z-popover`, `z-toast` | Owned by the overlays; feature code never sets a z-index |
| Motion | `--duration-fast`, `--duration-normal`, `--duration-slow`, `ease-standard` | Every animation; the durations become zero under a reduced-motion preference, and the skeleton pulses only when motion is allowed |

The shadcn recipe colours, `background`, `foreground`, `card`, `popover`, `primary`, `secondary`, `muted`, `accent`, `destructive`, `border`, `input` and `ring`, belong to the primitives. Feature code reaches them only through a primitive's variants, never as utilities of its own. The `chart-*` and `sidebar-*` tokens are reserved: nothing uses them, and nothing should until a component needs them. `StatusBadge` is the only status vocabulary; `Badge` names things and has no destructive variant.

### Themes and widths

Dark is the default and the primary operational theme. A responder can choose dark, light or the system's preference from the shell's header; the choice is kept per browser, and a script in the document head applies it before the first paint, so the page never flashes the other theme.

The responder surface supports desktop and tablet widths, from 768 px. Below 768 px the shell's primary navigation folds into a menu so the header never wraps. Tables never stack: a wide table scrolls horizontally inside its own container. Side-by-side layouts use the `lg` breakpoint and fall back to one column. Phone layouts are out of scope until a journey needs them.

### Visualization exception

The evidence graph is the one visualization canvas (styling rule 5): it positions nodes in SVG units, so its geometry constants live in its own file and are not tokens. Its colours still come from the semantic tokens, and every relation it draws is also listed as text.

## Component layers

```text
Primitives
  Button, Input, Textarea, Select, Checkbox, Label, Dialog, Tooltip, Table, Badge

Domain components
  IncidentStatusBadge, SeverityIndicator, ConfidenceDisplay,
  EvidenceDirectionBadge, ApprovalState, RecoveryState

Product patterns
  IncidentTimeline, EvidenceGraph, HypothesisComparison,
  RemediationReview, AgentActivity, RecoveryPanel,
  Field (label, control, description, error), ActionResult, ThemeSelect

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

The design-system half of this work is delivered by [Phase E02 – Frontend design-system depth](../implementation/phase-e02-frontend-design-system-depth.md), from the gap assessment below.

- Complete domain components and product patterns for all responder and administrator journeys.
- Validate responsive layouts, large evidence sets and reconnect/degraded states.
- Complete accessibility, theme and visual-regression coverage.
- Use real workflow states and contracts rather than mock-only presentation states.

## Gap assessment (2026-10-30)

A review of the implemented system against this plan, two days after the Phase 00 foundation landed. The stack and the rules are right and unusually well enforced; the gaps are breadth and the second half of the token model, and [Phase E02](../implementation/phase-e02-frontend-design-system-depth.md) closes them.

What works:

- Enforcement is real: Tailwind's default palette is removed, the lint rule rejects arbitrary values and palette classes in feature code, inline styles and competing libraries fail lint, and stylesheets may live only in the design-system folder.
- The semantic vocabulary matches what a responder must distinguish, every tone has a strong and a subtle step in both themes, and the foundation palette chooses its four steps for contrast.
- Accessibility is built in: status badges pair icon with label, data states announce as `status` or `alert`, the shell has a skip link, cards are labelled by their titles, the evidence graph lists every relation as text, and all 56 stories pass axe at WCAG 2.2 AA and a screenshot in both themes.
- `DataState`, `ConfidenceDisplay` and `HypothesisComparison` encode product rules, not only looks.

What is missing:

| Gap | Evidence | Consequence |
| --- | --- | --- |
| Breadth | Ten primitives; no input, label, select, checkbox, tabs, alert, dropdown or toast | Feature code writes the label-plus-control block and the server-action result paragraph by hand in five or six places |
| Token model | Colour, one radius and three durations exist; typography, spacing, elevation and z-index do not; the motion tokens are unused | Headings and labels use raw `text-sm` and `text-xl`; numbers rarely use tabular figures; the lint rule cannot cover tokens that do not exist |
| Two colour vocabularies | Sentinel semantic tokens beside the shadcn recipe set, with chart and sidebar tokens unused and a destructive `Badge` beside `StatusBadge` | Authors must know which set is meant for them |
| Theme selection | The root layout hard-codes dark; the light theme is reachable only in Storybook | Styling rule 7 is not true in the app |
| Responsive and platform coverage | One 1280 px viewport in Playwright; a handful of `lg:` breakpoints; Windows-only screenshot baselines | Narrow screens are untested; the CI acceptance criterion stays open |
| Small inconsistencies | `cn` from an npm package while the shadcn config aliases a local utility; a hard-coded locale; the graph's pixel constants not declared as the visualization exception | Minor drift from the rules |

Phase E02 closed these gaps on 2026-10-30, apart from the second platform's screenshot baselines, which the owner deferred until CI exists: feature code composes `Field` and `ActionResult` and writes no label, input, select or result message by hand; the token model above exists and the lint rule covers it, with fixture tests; the motion tokens drive every animation; one status vocabulary; theme selection in the app, with an incident journey in light; the tablet width in Playwright; interaction stories for the decision dialogs and the editors; `cn` from the local utility; and one display locale.

## Acceptance criteria

- The golden path uses approved components and semantic tokens end to end.
- No ordinary screen introduces inline styling, raw palette values or an independent styling system.
- Every reusable component exposes documented states and controlled variants.
- Storybook demonstrates operational, error and permission states.
- Automated accessibility and visual-regression checks run in CI.
- A contributor can build a new screen without inventing typography, color, spacing or interaction behavior.

