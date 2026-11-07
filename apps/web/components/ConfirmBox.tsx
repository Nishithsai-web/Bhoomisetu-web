"use client";

import { useEffect, useRef, type ReactNode } from "react";

/**
 * The "are you sure?" box on My listings. It appears after a page navigation, so it takes focus itself;
 * otherwise screen-reader users land at the top of the page and may never hear the question.
 */
export function ConfirmBox({ id, question, children }: { id: string; question: string; children: ReactNode }) {
  const ref = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    ref.current?.focus();
    ref.current?.scrollIntoView({ block: "center" });
  }, []);

  return (
    <div ref={ref} className="notice" role="alertdialog" aria-labelledby={id} tabIndex={-1}>
      <p id={id}>{question}</p>
      {children}
    </div>
  );
}
