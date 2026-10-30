// The theme a responder works in (Phase E02): dark, the primary operational theme and the default; light; or whatever
// the system prefers. The choice is kept per browser, and the document head applies it before the first paint, so the
// page never flashes the other theme.

export const themeStorageKey = "sentinel-theme";

export const themeChoices = [
  { value: "dark", label: "Dark" },
  { value: "light", label: "Light" },
  { value: "system", label: "System" },
] as const;

export type ThemeChoice = (typeof themeChoices)[number]["value"];

const changedEvent = "sentinel-theme-change";

export function readTheme(): ThemeChoice {
  try {
    const stored = window.localStorage.getItem(themeStorageKey);
    return stored === "light" || stored === "system" ? stored : "dark";
  } catch {
    return "dark";
  }
}

export function storeTheme(choice: ThemeChoice): void {
  try {
    window.localStorage.setItem(themeStorageKey, choice);
  } catch {
    // A browser that refuses storage still switches for this page.
  }
  window.dispatchEvent(new Event(changedEvent));
}

export function applyTheme(choice: ThemeChoice): void {
  const dark = choice === "dark" || (choice === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
  document.documentElement.classList.toggle("dark", dark);
  document.documentElement.style.colorScheme = dark ? "dark" : "light";
}

export function subscribeTheme(onChange: () => void): () => void {
  window.addEventListener("storage", onChange);
  window.addEventListener(changedEvent, onChange);
  return () => {
    window.removeEventListener("storage", onChange);
    window.removeEventListener(changedEvent, onChange);
  };
}

// The same decision as applyTheme, as a script the document head runs before anything is painted.
export const themeScript = `(function(){try{var c=localStorage.getItem(${JSON.stringify(themeStorageKey)});var d=c==="light"?false:c==="system"?matchMedia("(prefers-color-scheme: dark)").matches:true;document.documentElement.classList.toggle("dark",d);document.documentElement.style.colorScheme=d?"dark":"light"}catch(e){}})()`;
