import type { Decorator, Preview } from "@storybook/nextjs-vite";
import { Geist, Geist_Mono } from "next/font/google";
import { type ReactNode, useEffect } from "react";

import { TooltipProvider } from "../src/design-system/primitives/tooltip";

import "../src/app/globals.css";

const geistSans = Geist({ variable: "--font-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

// Mirrors the root layout: fonts on <html>, dark by default, light when the toolbar selects it.
function ThemeRoot({ theme, children }: { theme: "dark" | "light"; children: ReactNode }) {
  useEffect(() => {
    const root = document.documentElement;
    root.classList.add(geistSans.variable, geistMono.variable, "antialiased");
    root.classList.toggle("dark", theme === "dark");
  }, [theme]);

  return (
    <TooltipProvider>
      <div className="bg-background p-6 font-sans text-foreground">{children}</div>
    </TooltipProvider>
  );
}

const withSentinelTheme: Decorator = (Story, context) => (
  <ThemeRoot theme={context.globals.theme === "light" ? "light" : "dark"}>
    <Story />
  </ThemeRoot>
);

const preview: Preview = {
  decorators: [withSentinelTheme],
  globalTypes: {
    theme: {
      description: "Sentinel theme",
      toolbar: {
        title: "Theme",
        icon: "mirror",
        items: [
          { value: "dark", title: "Dark" },
          { value: "light", title: "Light" },
        ],
        dynamicTitle: true,
      },
    },
  },
  initialGlobals: { theme: "dark" },
  parameters: {
    layout: "fullscreen",
    a11y: { test: "error" },
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i,
      },
    },
  },
};

export default preview;
