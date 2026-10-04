/// <reference types="node" />

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

const tokensCss = readFileSync(
  resolve(process.cwd(), "src/styles/design-tokens.css"),
  "utf8",
);
const indexCss = readFileSync(resolve(process.cwd(), "src/index.css"), "utf8");

const readDeclarations = (selector: string) => {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const block = tokensCss.match(
    new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`),
  )?.[1];

  if (!block) {
    throw new Error(`Missing token block: ${selector}`);
  }

  return new Map(
    [...block.matchAll(/(--[\w-]+):\s*([^;]+);/g)].map((match) => [
      match[1],
      match[2].trim(),
    ]),
  );
};

const resolveColor = (
  name: string,
  declarations: Map<string, string>,
): string => {
  const value = declarations.get(`--ui-${name}`);
  if (!value) {
    throw new Error(`Missing semantic color: ${name}`);
  }

  const reference = value.match(/^var\((--[\w-]+)\)$/)?.[1];
  if (!reference) {
    return value;
  }

  const resolved = declarations.get(reference);
  if (!resolved) {
    throw new Error(`Missing palette color: ${reference}`);
  }

  return resolved;
};

const relativeLuminance = (hex: string) => {
  const channels = [1, 3, 5].map((start) =>
    Number.parseInt(hex.slice(start, start + 2), 16),
  );
  const [red, green, blue] = channels.map((value) => {
    const channel = value / 255;
    return channel <= 0.04045
      ? channel / 12.92
      : ((channel + 0.055) / 1.055) ** 2.4;
  });

  return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
};

const contrast = (foreground: string, background: string) => {
  const luminances = [
    relativeLuminance(foreground),
    relativeLuminance(background),
  ].sort((first, second) => second - first);
  return (luminances[0] + 0.05) / (luminances[1] + 0.05);
};

const semanticTokens = [
  "background",
  "surface",
  "surface-raised",
  "surface-subtle",
  "text",
  "text-muted",
  "text-inverse",
  "border",
  "border-strong",
  "primary",
  "primary-hover",
  "primary-text",
  "focus",
  "overlay",
  "shadow",
  "success-surface",
  "success-text",
  "success-border",
  "warning-surface",
  "warning-text",
  "warning-border",
  "danger-surface",
  "danger-text",
  "danger-border",
  "disabled-surface",
  "disabled-text",
  "disabled-border",
] as const;

describe("semantic design tokens", () => {
  it("defines the complete light and dark semantic contracts", () => {
    expect(tokensCss).toContain(":root {");
    expect(tokensCss).toContain(':root[data-theme="dark"]');

    for (const token of semanticTokens) {
      expect(tokensCss.match(new RegExp(`--ui-${token}:`, "g"))).toHaveLength(
        2,
      );
      expect(indexCss).toContain(`--color-${token}: var(--ui-${token});`);
    }
  });

  it("keeps current utilities mapped to the light semantic contract", () => {
    expect(indexCss).toContain("--color-ink: var(--ui-text);");
    expect(indexCss).toContain("--color-ink-soft: var(--ui-text-muted);");
    expect(indexCss).toContain("--color-brand-dark: var(--ui-primary);");
    expect(indexCss).toContain("--color-cream: var(--ui-background);");
  });

  it.each([
    ["light", ":root"],
    ["dark", ':root[data-theme="dark"]'],
  ])(
    "meets the documented contrast floor in the %s contract",
    (_, selector) => {
      const light = readDeclarations(":root");
      const declarations =
        selector === ":root"
          ? light
          : new Map([...light, ...readDeclarations(selector)]);

      const textPairs = [
        ["text", "background"],
        ["text", "surface"],
        ["text-muted", "background"],
        ["text-muted", "surface"],
        ["primary-text", "primary"],
        ["primary-text", "primary-hover"],
        ["success-text", "success-surface"],
        ["warning-text", "warning-surface"],
        ["danger-text", "danger-surface"],
        ["disabled-text", "disabled-surface"],
      ] as const;
      const nonTextPairs = [
        ["border", "background"],
        ["border", "surface"],
        ["border-strong", "background"],
        ["focus", "background"],
        ["focus", "surface"],
        ["success-border", "success-surface"],
        ["warning-border", "warning-surface"],
        ["danger-border", "danger-surface"],
        ["disabled-border", "disabled-surface"],
      ] as const;

      for (const [foreground, background] of textPairs) {
        expect(
          contrast(
            resolveColor(foreground, declarations),
            resolveColor(background, declarations),
          ),
          `${foreground} on ${background}`,
        ).toBeGreaterThanOrEqual(4.5);
      }

      for (const [foreground, background] of nonTextPairs) {
        expect(
          contrast(
            resolveColor(foreground, declarations),
            resolveColor(background, declarations),
          ),
          `${foreground} on ${background}`,
        ).toBeGreaterThanOrEqual(3);
      }
    },
  );
});
