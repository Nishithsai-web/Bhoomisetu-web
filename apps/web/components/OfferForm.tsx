"use client";

import { useActionState } from "react";
import { createOfferAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";

type OfferText = Dictionary["offer"];

export function OfferForm({ locale, parcelId, t, errors }: { locale: string; parcelId: string; t: OfferText; errors: Record<string, string> }) {
  const [state, action, pending] = useActionState<FormState, FormData>(createOfferAction, {});
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (errors[code] ?? errors.generic) : undefined;
  };

  return (
    <form action={action} className="card form big" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      {state.error && (
        <p role="alert" className="notice error">
          {errors[state.error] ?? errors.generic}
        </p>
      )}

      <fieldset className={fieldError("listerKind") ? "choices has-error" : "choices"}>
        <legend>{t.listerKind}</legend>
        {(["owner", "agent"] as const).map((kind) => (
          <label key={kind} className="choice">
            <input type="radio" name="listerKind" value={kind} defaultChecked={(v.listerKind ?? "owner") === kind} />
            {t.listerKinds[kind]}
          </label>
        ))}
        {fieldError("listerKind") && <p className="error">{fieldError("listerKind")}</p>}
      </fieldset>

      <Field id="askingPrice" label={t.askingPrice} hint={t.askingPriceHint} error={fieldError("askingPrice")}>
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
        {t.submit}
      </button>
    </form>
  );
}
