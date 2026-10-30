// Rejects raw design values in class strings so feature code composes semantic tokens
// and design-system variants (frontend design system, styling rule 2): arbitrary values,
// raw palette colors, raw type sizes, raw z-index values and raw shadows.
//
// Checked: className attributes and arguments to cn(), cva(), clsx() and cx().
// Allowed: bracketed variants such as `data-[state=open]:` or `has-[>svg]:`, because they
// select state rather than introduce a value.

const CLASS_FUNCTIONS = new Set(["cn", "cva", "clsx", "cx"]);

const PALETTE_COLOR =
  /^(?:bg|text|border(?:-[trblxyse])?|ring|ring-offset|fill|stroke|from|via|to|outline|decoration|shadow|accent|caret|divide|placeholder)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-(?:50|[1-9]00|950)(?:\/\d+)?$/;

// Tailwind's own type scale; feature code uses the typography tokens (text-body, text-caption, ...).
const TYPE_SIZE = /^text-(?:xs|sm|base|lg|xl|[2-9]xl)$/;

// Stacking order belongs to the design system's overlays; feature code never sets it.
const Z_INDEX = /^z-(?:\d+|auto)$/;

// Tailwind's own shadows; feature code uses the elevation tokens (shadow-raised, shadow-overlay).
const SHADOW = /^shadow(?:-(?:2xs|xs|sm|md|lg|xl|2xl|none))?$/;

/** Returns the utility part of a class token, after its variants. */
function utilityOf(token) {
  let depth = 0;
  let start = 0;
  for (let index = 0; index < token.length; index++) {
    const character = token[index];
    if (character === "[") depth++;
    else if (character === "]") depth--;
    else if (character === ":" && depth === 0) start = index + 1;
  }
  return token.slice(start).replace(/^!|!$/g, "").replace(/^-/, "");
}

function problemFor(token) {
  const utility = utilityOf(token);
  if (utility.includes("[")) return "arbitraryValue";
  if (PALETTE_COLOR.test(utility)) return "paletteColor";
  if (TYPE_SIZE.test(utility)) return "typeSize";
  if (Z_INDEX.test(utility)) return "zIndex";
  if (SHADOW.test(utility)) return "shadow";
  return null;
}

function isClassContext(node) {
  for (let current = node.parent; current; current = current.parent) {
    if (current.type === "JSXAttribute") {
      return current.name?.name === "className";
    }
    if (current.type === "CallExpression") {
      return current.callee.type === "Identifier" && CLASS_FUNCTIONS.has(current.callee.name);
    }
    if (current.type.includes("Function") || current.type === "Program") {
      return false;
    }
  }
  return false;
}

const rule = {
  meta: {
    type: "problem",
    docs: {
      description: "Disallow arbitrary values and raw palette colors in class strings.",
    },
    messages: {
      arbitraryValue:
        "'{{token}}' uses an arbitrary value. Use a semantic token or a design-system variant instead.",
      paletteColor:
        "'{{token}}' uses a raw palette color. Use a semantic color token such as bg-surface-panel or text-incident-critical.",
      typeSize:
        "'{{token}}' uses a raw type size. Use a typography token: text-display, text-title, text-body, text-label, text-caption or text-code.",
      zIndex: "'{{token}}' sets a stacking order. Overlays come from the design system, which owns the z-index tokens.",
      shadow: "'{{token}}' uses a raw shadow. Use an elevation token: shadow-raised or shadow-overlay.",
    },
    schema: [],
  },
  create(context) {
    function check(node, text) {
      if (!isClassContext(node)) return;
      for (const token of text.split(/\s+/).filter(Boolean)) {
        const messageId = problemFor(token);
        if (messageId) context.report({ node, messageId, data: { token } });
      }
    }

    return {
      Literal(node) {
        if (typeof node.value === "string") check(node, node.value);
      },
      TemplateElement(node) {
        check(node, node.value.cooked ?? node.value.raw);
      },
    };
  },
};

const plugin = {
  meta: { name: "sentinel-design-system" },
  rules: { "no-raw-design-values": rule },
};

export default plugin;
