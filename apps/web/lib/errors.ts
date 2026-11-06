import type { Dictionary } from "./i18n.ts";

/** Plain-language message for an API or form error code, falling back to a generic message. */
export function errorText(t: Dictionary, code: string | undefined): string {
  if (!code) return t.errors.generic;
  const messages = t.errors as Record<string, string>;
  return messages[code] ?? t.errors.generic;
}
