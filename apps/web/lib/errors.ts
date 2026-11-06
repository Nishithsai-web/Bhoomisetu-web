import type { Dictionary } from "./i18n.ts";

/** Plain-language message for an API or form error code, falling back to a generic message. */
export function errorText(t: Dictionary, code: string | undefined): string {
  if (!code) return t.errors.generic;
  const messages = t.errors as Record<string, string>;
  // Own keys only: a code such as "constructor" from a URL must not read the object prototype.
  return Object.hasOwn(messages, code) ? (messages[code] as string) : t.errors.generic;
}
