"use client";

import { useEffect, useRef } from "react";

type Turnstile = {
  render(element: HTMLElement, options: Record<string, unknown>): string;
  remove(widgetId: string): void;
};

declare global {
  interface Window {
    turnstile?: Turnstile;
  }
}

const SCRIPT = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
let loading: Promise<void> | null = null;

/** Loads the Turnstile script once per page, however many widgets ask for it. */
function loadTurnstile(): Promise<void> {
  if (window.turnstile) return Promise.resolve();
  loading ??= new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = SCRIPT;
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => {
      loading = null; // allow a retry on the next render
      reject(new Error("Turnstile could not load"));
    };
    document.head.appendChild(script);
  });
  return loading;
}

/**
 * Cloudflare Turnstile human check, rendered explicitly. A token works once, so the widget is rendered
 * again whenever `resetKey` changes (pass the form state: every server reply gets a fresh check).
 * The token is posted as the hidden field "cf-turnstile-response" inside the surrounding form.
 */
export function HumanCheck({ siteKey, resetKey }: { siteKey: string; resetKey: unknown }) {
  const ref = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    let widgetId: string | undefined;
    let cancelled = false;
    loadTurnstile()
      .then(() => {
        if (cancelled || !ref.current || !window.turnstile) return;
        widgetId = window.turnstile.render(ref.current, { sitekey: siteKey, language: "auto", "response-field-name": "cf-turnstile-response" });
      })
      .catch(() => {
        // Without the widget the API answers captcha.required, which the form shows as a plain message.
      });
    return () => {
      cancelled = true;
      if (widgetId && window.turnstile) window.turnstile.remove(widgetId);
    };
  }, [siteKey, resetKey]);

  return <div ref={ref} className="human-check" />;
}
