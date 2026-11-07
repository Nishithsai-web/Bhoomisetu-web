"use client";

import { useParams } from "next/navigation";

// Shown when a page or form fails unexpectedly (for example a file far too large for the server).
// Kept tiny and self-contained so it works even when the rest of the page could not load.
const TEXT: Record<string, { message: string; retry: string; home: string }> = {
  en: { message: "Something went wrong. Please go back and try again.", retry: "Try again", home: "Home" },
  te: { message: "ఏదో పొరపాటు జరిగింది. వెనక్కి వెళ్లి మళ్లీ ప్రయత్నించండి.", retry: "మళ్లీ ప్రయత్నించండి", home: "హోమ్" },
  hi: { message: "कुछ गड़बड़ हो गई। वापस जाकर फिर कोशिश करें।", retry: "फिर कोशिश करें", home: "होम" },
};

export default function LocaleError({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const params = useParams<{ locale?: string }>();
  const locale = params?.locale && Object.hasOwn(TEXT, params.locale) ? params.locale : "en";
  const text = TEXT[locale];
  return (
    <section className="page narrow">
      <p className="notice error" role="alert">
        {text.message}
      </p>
      <p className="form-inline">
        <button type="button" onClick={reset}>
          {text.retry}
        </button>{" "}
        <a href={`/${locale}`}>{text.home}</a>
      </p>
    </section>
  );
}
