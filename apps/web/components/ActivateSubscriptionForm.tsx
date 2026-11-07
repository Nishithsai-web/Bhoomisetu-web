"use client";

import { useActionState } from "react";
import { activateSubscriptionAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback } from "./FormFeedback";

/** Reviewer records a subscription after receiving the payment (UPI or receipt) outside the app. */
export function ActivateSubscriptionForm({ locale, t }: { locale: string; t: Dictionary }) {
  const [state, action, pending] = useActionState<FormState, FormData>(activateSubscriptionAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const errors = t.errors as Record<string, string>;
  const x = t.reviewExtra;
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };

  return (
    <form ref={formRef} action={action} className="card form" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <ErrorSummary state={state} text={t.form} errors={errors} summaryRef={summaryRef} />
      <Field id="sub-phone" label={x.phone} error={fieldError("phone")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => <input id="sub-phone" name="phone" type="tel" defaultValue={v.phone ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />}
      </Field>
      <Field id="sub-audience" label={x.audience} error={fieldError("audience")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <select id="sub-audience" name="audience" defaultValue={v.audience ?? "lister"} aria-describedby={describedBy} aria-invalid={invalid}>
            <option value="lister">{x.audienceLister}</option>
            <option value="viewer">{x.audienceViewer}</option>
          </select>
        )}
      </Field>
      <Field id="sub-months" label={x.months} error={fieldError("months")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <input id="sub-months" name="months" inputMode="numeric" defaultValue={v.months ?? "1"} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <Field id="sub-amount" label={x.amount} error={fieldError("amount")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <input id="sub-amount" name="amount" inputMode="decimal" defaultValue={v.amount ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <Field id="sub-reference" label={x.paymentReference} error={fieldError("paymentReference")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <input id="sub-reference" name="paymentReference" maxLength={100} defaultValue={v.paymentReference ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <button type="submit" disabled={pending}>
        {pending ? t.form.sending : x.activate}
      </button>
    </form>
  );
}
