"use client";

import { useActionState, useState } from "react";
import { uploadDocumentAction, type FormState } from "@/app/[locale]/actions";
import type { DocumentType } from "@/lib/api";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback } from "./FormFeedback";

/**
 * Lister adds one public document. The dropdown lists only the allowed types; the number field appears
 * as required for papers that carry one (HMDA/DTCP approval, RERA certificate, building permission).
 */
export function UploadDocumentForm({ locale, parcelId, types, t }: { locale: string; parcelId: string; types: DocumentType[]; t: Dictionary }) {
  const [state, action, pending] = useActionState<FormState, FormData>(uploadDocumentAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const d = t.documents;
  const errors = t.errors as Record<string, string>;
  const labels = d.types as Record<string, string>;
  const v = state.values ?? {};
  const [type, setType] = useState(v.type ?? "");
  const selected = types.find((x) => x.code === type);
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };

  return (
    <form ref={formRef} action={action} className="card form" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      <ErrorSummary state={state} text={t.form} errors={errors} summaryRef={summaryRef} />
      <Field id="type" label={d.type} hint={d.typeHint} error={fieldError("type")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <select id="type" name="type" value={type} onChange={(e) => setType(e.target.value)} aria-describedby={describedBy} aria-invalid={invalid}>
            <option value="">{t.form.choose}</option>
            {types.map((x) => (
              <option key={x.code} value={x.code}>
                {labels[x.code] ?? x.code}
              </option>
            ))}
          </select>
        )}
      </Field>
      <Field id="referenceNumber" label={d.referenceLabel} hint={d.referenceHint} error={fieldError("referenceNumber")}
        requiredMark={selected?.referenceRequired ? t.form.requiredMark : undefined}>
        {(describedBy, invalid) => (
          <input id="referenceNumber" name="referenceNumber" maxLength={60} defaultValue={v.referenceNumber ?? ""} autoComplete="off"
            aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <Field id="file" label={d.file} hint={d.fileHint} error={fieldError("file")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => (
          <input id="file" name="file" type="file" accept={selected?.imagesOnly ? "image/jpeg,image/png,image/webp" : "application/pdf,image/jpeg,image/png,image/webp"}
            aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
      <Field id="declaration" label={d.declaration} error={fieldError("declaration")}>
        {(describedBy, invalid) => <input id="declaration" name="declaration" type="checkbox" aria-describedby={describedBy} aria-invalid={invalid} />}
      </Field>
      <button type="submit" disabled={pending}>
        {pending ? t.form.sending : d.upload}
      </button>
    </form>
  );
}
