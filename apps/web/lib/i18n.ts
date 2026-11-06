import en from "../messages/en.json";
import te from "../messages/te.json";
import hi from "../messages/hi.json";

export const LOCALES = ["en", "te", "hi"] as const;
export type Locale = (typeof LOCALES)[number];

/** English is the reference; scripts/check-translations.mjs fails the build if te or hi miss a key. */
export type Dictionary = typeof en;

const DICTIONARIES: Record<Locale, Dictionary> = { en, te, hi };

/** BCP 47 tags for number and date formatting; Indian grouping (12,34,567) in every language. */
export const INTL_LOCALE: Record<Locale, string> = { en: "en-IN", te: "te-IN", hi: "hi-IN" };

export const LANGUAGE_NAMES: Record<Locale, string> = { en: "English", te: "తెలుగు", hi: "हिंदी" };

export function isLocale(value: unknown): value is Locale {
  return typeof value === "string" && (LOCALES as readonly string[]).includes(value);
}

export function getDictionary(locale: Locale): Dictionary {
  return DICTIONARIES[locale];
}

/** Fills {name} placeholders. Unknown placeholders are left visible so a missing value is noticed, not hidden. */
export function format(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    Object.prototype.hasOwnProperty.call(values, key) ? String(values[key]) : match,
  );
}
