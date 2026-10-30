import Link from "next/link";
import { Suspense } from "react";

import { AppShell } from "@/design-system/patterns/app-shell";
import { AdminNav, AdminNavLinks } from "@/features/admin/admin-nav";

// Every control-plane page shares the shell and the section navigation. The pathname of a
// configuration area outside the known ones is resolved only at request time, so the navigation
// waits for it behind Suspense instead of holding back the rest of the layout.
export default function AdminLayout({ children }: LayoutProps<"/admin">) {
  return (
    <AppShell
      navigation={
        <Link href="/" className="text-body text-text-muted hover:text-text-primary focus-visible:text-text-primary">
          Incidents
        </Link>
      }
    >
      <Suspense fallback={<AdminNavLinks />}>
        <AdminNav />
      </Suspense>
      {children}
    </AppShell>
  );
}
