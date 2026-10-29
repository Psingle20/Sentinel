import type { Meta, StoryObj } from "@storybook/nextjs-vite";

import { SectionNav, sectionNavLinkClass } from "@/design-system/patterns/section-nav";

const pages = ["Overview", "Policy", "SLOs and workflow", "Models", "Provider sources", "Tenants and notifications"];

const meta = {
  title: "Patterns/Section navigation",
  component: SectionNav,
  args: { label: "Control plane", children: null },
} satisfies Meta<typeof SectionNav>;

export default meta;

type Story = StoryObj<typeof meta>;

export const ControlPlane: Story = {
  args: {
    children: pages.map((page, index) => (
      <a key={page} href={`#${index}`} aria-current={index === 2 ? "page" : undefined} className={sectionNavLinkClass(index === 2)}>
        {page}
      </a>
    )),
  },
};
