"use client";

import { useActionState } from "react";
import { reportListingAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback } from "./FormFeedback";

/** "Report this listing": one reason from a short list, optional details (required for "other"). */
export function ReportForm({ locale, parcelId, reasons, t }: { locale: string; parcelId: string; reasons: readonly string[]; t: Dictionary }) {
  const [state, action, pending] = useActionState<FormState, FormData>(reportListingAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const r = t.report;
  const errors = t.errors as Record<string, string>;
  const labels = r.reasons as Record<string, string>;
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };
  const reasonError = fieldError("reason");

  return (
    <form ref={formRef} action={action} className="card form" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      <ErrorSummary state={state} text={t.form} errors={errors} summaryRef={summaryRef} />
      <fieldset aria-describedby={reasonError ? "reason-error" : undefined}>
        <legend>
          {r.reason} <span className="required">{t.form.requiredMark}</span>
        </legend>
        {reasonError && (
          <p id="reason-error" className="error">
            {reasonError}
          </p>
        )}
        {reasons.map((code) => (
          <label key={code} className="choice">
            <input type="radio" name="reason" value={code} required defaultChecked={v.reason === code} /> {labels[code] ?? code}
          </label>
        ))}
      </fieldset>
      <Field id="details" label={r.details} hint={r.detailsHint} error={fieldError("details")}>
        {(describedBy, invalid) => (
          <textarea id="details" name="details" rows={4} maxLength={1000} defaultValue={v.details ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <button type="submit" disabled={pending}>
        {pending ? t.form.sending : r.submit}
      </button>
    </form>
  );
}
