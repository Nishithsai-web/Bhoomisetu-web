"use client";

import { useEffect, useRef, type RefObject } from "react";
import type { FormState } from "@/app/[locale]/actions";

export type FormText = { fixBelow: string; fixOne: string; sending: string; requiredMark: string; choose: string };

/**
 * After a submit with problems: one announcement at the top ("Please check the 3 highlighted fields"),
 * then focus jumps to the first field to fix. People new to forms otherwise miss errors further down.
 */
export function useFormFeedback(state: FormState): { formRef: RefObject<HTMLFormElement | null>; summaryRef: RefObject<HTMLParagraphElement | null> } {
  const formRef = useRef<HTMLFormElement | null>(null);
  const summaryRef = useRef<HTMLParagraphElement | null>(null);

  useEffect(() => {
    if (state.errors && state.errors.length > 0) {
      const first = formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]');
      (first ?? summaryRef.current)?.focus();
    } else if (state.error) {
      summaryRef.current?.focus();
    }
  }, [state]);

  return { formRef, summaryRef };
}

export function ErrorSummary({ state, text, errors, summaryRef }: { state: FormState; text: FormText; errors: Record<string, string>; summaryRef: RefObject<HTMLParagraphElement | null> }) {
  const count = state.errors?.length ?? 0;
  if (count === 0 && !state.error) return null;

  const message = count > 0
    ? (count === 1 ? text.fixOne : text.fixBelow.replace("{count}", String(count)))
    : (Object.hasOwn(errors, state.error ?? "") ? errors[state.error as string] : errors.generic);

  return (
    <p ref={summaryRef} role="alert" tabIndex={-1} className="notice error">
      {message}
    </p>
  );
}
