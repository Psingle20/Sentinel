"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

import { SectionNav, sectionNavLinkClass } from "@/design-system/patterns/section-nav";

import { areaTitles } from "./areas";

const pages = [
  { href: "/admin", label: "Overview" },
  { href: "/admin/policy", label: "Policy" },
  { href: "/admin/configuration/runtime", label: areaTitles.runtime },
  { href: "/admin/configuration/models", label: areaTitles.models },
  { href: "/admin/configuration/sources", label: areaTitles.sources },
  { href: "/admin/configuration/api", label: areaTitles.api },
] as const;

// The links alone, with nothing marked current: what renders while the pathname is not yet known.
export function AdminNavLinks({ pathname }: { pathname?: string }) {
  return (
    <SectionNav label="Control plane">
      {pages.map((page) => {
        const current = pathname === page.href;
        return (
          <Link key={page.href} href={page.href} aria-current={current ? "page" : undefined} className={sectionNavLinkClass(current)}>
            {page.label}
          </Link>
        );
      })}
    </SectionNav>
  );
}

export function AdminNav() {
  return <AdminNavLinks pathname={usePathname()} />;
}
