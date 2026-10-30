import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { TooltipProvider } from "@/design-system/primitives/tooltip";
import { themeScript } from "@/lib/theme";

import { HydrationMarker } from "./hydration-marker";

import "./globals.css";

const geistSans = Geist({
  variable: "--font-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Sentinel",
  description: "Evidence-backed incident investigation and recovery.",
};

// Dark is the primary operational theme and the default (frontend design system, styling rule 7). The head script
// applies a responder's stored choice before the first paint, so the server's class may change before hydration.
export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`dark ${geistSans.variable} ${geistMono.variable} h-full antialiased`}
    >
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body className="flex min-h-full flex-col">
        <TooltipProvider>{children}</TooltipProvider>
        <HydrationMarker />
      </body>
    </html>
  );
}
