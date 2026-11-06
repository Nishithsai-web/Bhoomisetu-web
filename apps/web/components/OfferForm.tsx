"use client";

import { useActionState } from "react";
import { createOfferAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback, type FormText } from "./FormFeedback";

type OfferText = Dictionary["offer"];

export function OfferForm({ locale, parcelId, t, form, errors }: { locale: string; parcelId: string; t: OfferText; form: FormText; errors: Record<string, string> }) {
  const [state, action, pending] = useActionState<FormState, FormData>(createOfferAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };
  const kindError = fieldError("listerKind");

  return (
    <form ref={formRef} action={action} className="card form big" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      <ErrorSummary state={state} text={form} errors={errors} summaryRef={summaryRef} />

      {/* No answer is pre-selected: agents must not become "owners" by accepting a default. */}
      <fieldset className={kindError ? "choices has-error" : "choices"} aria-describedby={kindError ? "listerKind-error" : undefined}>
        <legend>
          {t.listerKind} <span className="required">{form.requiredMark}</span>
        </legend>
        {(["owner", "agent"] as const).map((kind) => (
          <label key={kind} className="choice">
            <input type="radio" name="listerKind" value={kind} defaultChecked={v.listerKind === kind} aria-invalid={Boolean(kindError)} required />
            {t.listerKinds[kind]}
          </label>
        ))}
        {kindError && (
          <p id="listerKind-error" className="error">
            {kindError}
          </p>
        )}
      </fieldset>

      <Field id="askingPrice" label={t.askingPrice} hint={t.askingPriceHint} error={fieldError("askingPrice")} requiredMark={form.requiredMark}>
        {(describedBy, invalid) => (
          <input id="askingPrice" name="askingPrice" inputMode="decimal" defaultValue={v.askingPrice ?? ""} required aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>

      <Field id="brokeragePercent" label={t.brokeragePercent} hint={t.brokeragePercentHint} error={fieldError("brokeragePercent")}>
        {(describedBy, invalid) => (
          <input id="brokeragePercent" name="brokeragePercent" inputMode="decimal" defaultValue={v.brokeragePercent ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>

      <Field id="brokerageTerms" label={t.brokerageTerms} hint={t.brokerageTermsHint} error={fieldError("brokerageTerms")}>
        {(describedBy, invalid) => (
          <textarea id="brokerageTerms" name="brokerageTerms" maxLength={500} rows={3} defaultValue={v.brokerageTerms ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>

      <p className="hint">{t.replaceNote}</p>
      <button type="submit" disabled={pending}>
        {pending ? form.sending : t.submit}
      </button>
    </form>
  );
}
