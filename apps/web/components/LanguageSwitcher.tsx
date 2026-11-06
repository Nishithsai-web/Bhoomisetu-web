"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { LANGUAGE_NAMES, LOCALES, type Locale } from "@/lib/i18n";

/**
 * Switches language on the same page, keeping the rest of the path. The query string is not carried over:
 * reading it needs useSearchParams, which would force a Suspense boundary around every static page.
 */
export function LanguageSwitcher({ current, label }: { current: Locale; label: string }) {
  const pathname = usePathname() ?? `/${current}`;
  const rest = pathname.replace(/^\/(en|te|hi)(?=\/|$)/, "");

  return (
    <nav aria-label={label} className="language-switcher">
      {LOCALES.map((locale) => (
        <Link
          key={locale}
          href={`/${locale}${rest}`}
          hrefLang={locale}
          lang={locale}
          aria-current={locale === current ? "true" : undefined}
          className={locale === current ? "active" : undefined}
        >
          {LANGUAGE_NAMES[locale]}
        </Link>
      ))}
    </nav>
  );
}
