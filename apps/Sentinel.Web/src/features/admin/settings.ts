// The single-value settings of a configuration document, for the structured editor (Phase 08): those at the top level and
// those one object down, such as the workflow's timers. Lists and deeper objects, such as detection rules, stay in the
// document editor. The owning service still validates whatever is published.

export type SettingValue = string | number | boolean;

export type Setting = { key: string; label: string; value: SettingValue };

export type SettingsSection = { key: string; title: string; settings: Setting[] };

type Document = Record<string, unknown>;

export function settingsOf(document: Document): SettingsSection[] {
  const general: Setting[] = [];
  const sections: SettingsSection[] = [];
  for (const [name, value] of Object.entries(document)) {
    if (name === "version") {
      continue;
    }

    if (isSetting(value)) {
      general.push({ key: name, label: humanize(name), value });
    } else if (isObject(value)) {
      const settings = Object.entries(value)
        .filter((entry): entry is [string, SettingValue] => isSetting(entry[1]))
        .map(([child, childValue]) => ({ key: `${name}.${child}`, label: humanize(child), value: childValue }));
      if (settings.length > 0) {
        sections.push({ key: name, title: humanize(name), settings });
      }
    }
  }

  return general.length > 0 ? [{ key: "", title: "General", settings: general }, ...sections] : sections;
}

// The document with the edited settings and the new version. A number that no longer parses is published as typed, so
// the owning service refuses it with its own message.
export function withSettings(document: Document, values: Record<string, SettingValue>, version: string): Document {
  const next: Document = structuredClone(document);
  next.version = version;
  for (const [key, value] of Object.entries(values)) {
    const [name, child] = key.split(".", 2);
    const target = child === undefined ? next : (next[name] as Document);
    target[child ?? name] = value;
  }

  return next;
}

// A number input's text back to the type the document had.
export function parsed(original: SettingValue, text: string): SettingValue {
  if (typeof original !== "number") {
    return text;
  }

  const number = Number(text);
  return text.trim() === "" || Number.isNaN(number) ? text : number;
}

export function humanize(name: string): string {
  const words = name.replace(/([a-z0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function isSetting(value: unknown): value is SettingValue {
  return typeof value === "string" || typeof value === "number" || typeof value === "boolean";
}

function isObject(value: unknown): value is Document {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
