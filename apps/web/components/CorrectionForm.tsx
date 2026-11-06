"use client";

import { useActionState, useState } from "react";
import { changeRequestAction, type FormState } from "@/app/[locale]/actions";
import { ACCESS_RIGHTS, CHANGE_FIELDS, FACINGS, LAND_CLASSES, ROAD_TYPES } from "@/lib/forms";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback, type FormText } from "./FormFeedback";

type ChangeField = (typeof CHANGE_FIELDS)[number];

/**
 * Asks for one correction at a time. The value box changes with the chosen detail: a list of choices for
 * things like road type, a number (in feet or metres) for lengths.
 */
export function CorrectionForm({ locale, parcelId, t, errors }: { locale: string; parcelId: string; t: Dictionary; errors: Record<string, string> }) {
  const [state, action, pending] = useActionState<FormState, FormData>(changeRequestAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const form: FormText = t.form;
  const v = state.values ?? {};
  const [field, setField] = useState<ChangeField>((CHANGE_FIELDS as readonly string[]).includes(v.field ?? "") ? (v.field as ChangeField) : "road_width_m");
  const c = t.correct;
  const s = t.sell;
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };

  const choices: Partial<Record<ChangeField, { options: readonly string[]; names: Record<string, string> }>> = {
    facing: { options: FACINGS, names: s.facings },
    road_type: { options: ROAD_TYPES, names: s.roadTypes },
    access_right: { options: ACCESS_RIGHTS, names: s.accessRights },
    land_class: { options: LAND_CLASSES, names: s.landClasses },
  };
  const choice = choices[field];
  const isLength = field === "road_width_m" || field === "frontage_m" || field === "depth_m";

  return (
    <form ref={formRef} action={action} className="card form big" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      <ErrorSummary state={state} text={form} errors={errors} summaryRef={summaryRef} />

      <Field id="field" label={c.field} error={fieldError("field")}>
        {(describedBy) => (
          <select id="field" name="field" value={field} onChange={(e) => setField(e.target.value as ChangeField)} aria-describedby={describedBy}>
            {CHANGE_FIELDS.map((f) => (
              <option key={f} value={f}>
                {c.fields[f]}
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field id="proposedValue" label={c.proposedValue} hint={choice ? undefined : c.proposedValueHint} error={fieldError("proposedValue")} requiredMark={form.requiredMark}>
        {(describedBy, invalid) =>
          choice ? (
            <select id="proposedValue" name="proposedValue" key={field} defaultValue={v.field === field ? (v.proposedValue ?? "") : ""} aria-describedby={describedBy} aria-invalid={invalid}>
              <option value="">{form.choose}</option>
              {choice.options.map((o) => (
                <option key={o} value={o}>
                  {choice.names[o] ?? o}
                </option>
              ))}
            </select>
          ) : (
            <input id="proposedValue" name="proposedValue" key={field} inputMode="decimal" defaultValue={v.proposedValue ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
          )
        }
      </Field>

      {isLength && (
        <Field id="lengthUnit" label={s.lengthUnit}>
          {(describedBy) => (
            <select id="lengthUnit" name="lengthUnit" defaultValue={v.lengthUnit ?? "feet"} aria-describedby={describedBy}>
              <option value="feet">{s.lengthUnits.feet}</option>
              <option value="metres">{s.lengthUnits.metres}</option>
            </select>
          )}
        </Field>
      )}

      <Field id="reason" label={c.reason} hint={c.reasonHint} error={fieldError("reason")} requiredMark={form.requiredMark}>
        {(describedBy, invalid) => (
          <textarea id="reason" name="reason" rows={3} maxLength={1000} defaultValue={v.reason ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>

      <p className="hint">{c.notListed}</p>
      <button type="submit" disabled={pending}>
        {pending ? form.sending : c.submit}
      </button>
    </form>
  );
}
