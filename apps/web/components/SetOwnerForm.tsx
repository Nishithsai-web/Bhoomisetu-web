"use client";

import { useActionState } from "react";
import { setOwnerAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback } from "./FormFeedback";

/** Reviewer records the owner of record by mobile number, after checking the documents. */
export function SetOwnerForm({ locale, t }: { locale: string; t: Dictionary }) {
  const [state, action, pending] = useActionState<FormState, FormData>(setOwnerAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const errors = t.errors as Record<string, string>;
  const r = t.review;
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };

  return (
    <form ref={formRef} action={action} className="card form" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <ErrorSummary state={state} text={t.form} errors={errors} summaryRef={summaryRef} />
      <Field id="parcelId" label={r.parcelId} hint={r.parcelIdHint} error={fieldError("parcelId")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => <input id="parcelId" name="parcelId" defaultValue={v.parcelId ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />}
      </Field>
      <Field id="phone" label={r.ownerPhone} error={fieldError("phone")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => <input id="phone" name="phone" type="tel" defaultValue={v.phone ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />}
      </Field>
      <Field id="evidence" label={r.evidence} hint={r.evidenceHint} error={fieldError("evidence")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <textarea id="evidence" name="evidence" rows={3} maxLength={1000} defaultValue={v.evidence ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <button type="submit" disabled={pending}>
        {pending ? t.form.sending : r.recordOwner}
      </button>
    </form>
  );
}
