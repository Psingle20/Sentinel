import type { Meta, StoryObj } from "@storybook/nextjs-vite";

// Class names are written out in full so Tailwind can detect them.
const roles = [
  { name: "text-display", className: "text-display", sample: "checkout-service" },
  { name: "text-title", className: "text-title", sample: "Leading hypothesis" },
  { name: "text-body", className: "text-body", sample: "The v2.4.1 deployment leaks a Redis connection on every second session." },
  { name: "text-label", className: "text-label", sample: "Approval timeout" },
  { name: "text-caption", className: "text-caption", sample: "14:02:31 · v18" },
  { name: "text-code", className: "font-mono text-code", sample: "sha256:3f9a…c1e0" },
];

function Typography() {
  return (
    <dl className="flex flex-col gap-4">
      {roles.map((role) => (
        <div key={role.name} className="flex flex-col gap-1">
          <dt className="font-mono text-code text-text-muted">{role.name}</dt>
          <dd className={`${role.className} text-text-primary`}>{role.sample}</dd>
        </div>
      ))}
      <div className="flex flex-col gap-1">
        <dt className="font-mono text-code text-text-muted">tabular-nums</dt>
        <dd className="text-body text-text-primary tabular-nums">0.42 · 1.96 · 10.00</dd>
      </div>
    </dl>
  );
}

const meta = {
  title: "Foundations/Typography",
  component: Typography,
} satisfies Meta<typeof Typography>;

export default meta;

export const Roles: StoryObj<typeof meta> = {};
