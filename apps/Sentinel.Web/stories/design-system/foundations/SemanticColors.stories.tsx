import type { Meta, StoryObj } from "@storybook/nextjs-vite";

// Class names are written out in full so Tailwind can detect them.
const statusTokens = [
  { name: "incident-critical", strong: "bg-incident-critical", subtle: "bg-incident-critical-subtle" },
  { name: "incident-warning", strong: "bg-incident-warning", subtle: "bg-incident-warning-subtle" },
  { name: "incident-stable", strong: "bg-incident-stable", subtle: "bg-incident-stable-subtle" },
  { name: "evidence-supporting", strong: "bg-evidence-supporting", subtle: "bg-evidence-supporting-subtle" },
  { name: "evidence-contradicting", strong: "bg-evidence-contradicting", subtle: "bg-evidence-contradicting-subtle" },
  { name: "evidence-neutral", strong: "bg-evidence-neutral", subtle: "bg-evidence-neutral-subtle" },
  { name: "hypothesis-leading", strong: "bg-hypothesis-leading", subtle: "bg-hypothesis-leading-subtle" },
  { name: "approval-required", strong: "bg-approval-required", subtle: "bg-approval-required-subtle" },
  { name: "action-executing", strong: "bg-action-executing", subtle: "bg-action-executing-subtle" },
  { name: "recovery-improving", strong: "bg-recovery-improving", subtle: "bg-recovery-improving-subtle" },
  { name: "recovery-failed", strong: "bg-recovery-failed", subtle: "bg-recovery-failed-subtle" },
];

const surfaceTokens = [
  { name: "surface-canvas", swatch: "bg-surface-canvas" },
  { name: "surface-panel", swatch: "bg-surface-panel" },
  { name: "text-primary", swatch: "bg-text-primary" },
  { name: "text-muted", swatch: "bg-text-muted" },
  { name: "border-subtle", swatch: "bg-border-subtle" },
];

function SemanticColors() {
  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-text-primary">Surfaces and text</h2>
        <ul className="grid grid-cols-5 gap-3">
          {surfaceTokens.map((token) => (
            <li key={token.name} className="flex flex-col gap-2">
              <span className={`h-12 rounded-md border border-border-subtle ${token.swatch}`} />
              <code className="font-mono text-xs text-text-muted">{token.name}</code>
            </li>
          ))}
        </ul>
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-sm font-semibold text-text-primary">Operational states</h2>
        <ul className="grid grid-cols-4 gap-3">
          {statusTokens.map((token) => (
            <li key={token.name} className="flex flex-col gap-2">
              <span className="flex h-12 overflow-hidden rounded-md border border-border-subtle">
                <span className={`flex-1 ${token.strong}`} />
                <span className={`flex-1 ${token.subtle}`} />
              </span>
              <code className="font-mono text-xs text-text-muted">{token.name} / -subtle</code>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

const meta = {
  title: "Foundations/Semantic colors",
  component: SemanticColors,
} satisfies Meta<typeof SemanticColors>;

export default meta;

export const Palette: StoryObj<typeof meta> = {};
