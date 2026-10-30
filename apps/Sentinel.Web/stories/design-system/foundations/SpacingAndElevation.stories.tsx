import type { Meta, StoryObj } from "@storybook/nextjs-vite";

// Class names are written out in full so Tailwind can detect them.
const steps = [
  { name: "1 (4 px)", className: "w-1" },
  { name: "2 (8 px)", className: "w-2" },
  { name: "3 (12 px)", className: "w-3" },
  { name: "4 (16 px)", className: "w-4" },
  { name: "6 (24 px)", className: "w-6" },
  { name: "8 (32 px)", className: "w-8" },
];

const elevations = [
  { name: "shadow-raised", className: "shadow-raised" },
  { name: "shadow-overlay", className: "shadow-overlay" },
];

function SpacingAndElevation() {
  return (
    <div className="flex flex-col gap-8">
      <section className="flex flex-col gap-3">
        <h2 className="text-title text-text-primary">Spacing</h2>
        <ul className="flex flex-col gap-2">
          {steps.map((step) => (
            <li key={step.name} className="flex items-center gap-3">
              <span className={`h-4 rounded-sm bg-text-muted ${step.className}`} />
              <span className="font-mono text-code text-text-muted">{step.name}</span>
            </li>
          ))}
        </ul>
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-title text-text-primary">Elevation</h2>
        <div className="flex gap-6">
          {elevations.map((elevation) => (
            <div key={elevation.name} className={`flex size-32 items-end rounded-lg bg-surface-panel p-3 ${elevation.className}`}>
              <span className="font-mono text-code text-text-muted">{elevation.name}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

const meta = {
  title: "Foundations/Spacing and elevation",
  component: SpacingAndElevation,
} satisfies Meta<typeof SpacingAndElevation>;

export default meta;

export const Scale: StoryObj<typeof meta> = {};
