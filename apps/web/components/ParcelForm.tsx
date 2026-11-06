"use client";

import { useActionState } from "react";
import { createParcelAction, type FormState } from "@/app/[locale]/actions";
import { ACCESS_RIGHTS, AREA_SOURCES, FACINGS, LAND_CLASSES, LENGTH_UNITS, ROAD_TYPES, ROAD_WIDTH_SOURCES } from "@/lib/forms";
import { AREA_UNITS, type AreaUnit } from "@/lib/units";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback, type FormText } from "./FormFeedback";

type SellText = Dictionary["sell"];

/**
 * The land listing form, in four short numbered sections with a hint under every field. Only location,
 * survey number, land type and area are required; everything else can be left empty and added later.
 */
export function ParcelForm({ locale, t, unitNames, form, errors }: { locale: string; t: SellText; unitNames: Record<AreaUnit, string>; form: FormText; errors: Record<string, string> }) {
  const [state, action, pending] = useActionState<FormState, FormData>(createParcelAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const v = state.values ?? {};
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };

  const text = (name: keyof SellText & string, required = false, inputMode?: "decimal" | "numeric") => {
    const hintKey = `${name}Hint` as keyof SellText;
    const hint = typeof t[hintKey] === "string" ? (t[hintKey] as string) : undefined;
    return (
      <Field id={name} label={t[name] as string} hint={hint} error={fieldError(name)} requiredMark={required ? form.requiredMark : undefined}>
        {(describedBy, invalid) => (
          <input id={name} name={name} defaultValue={v[name] ?? ""} required={required} inputMode={inputMode} aria-describedby={describedBy} aria-invalid={invalid} />
        )}
      </Field>
    );
  };

  const select = (name: string, label: string, options: readonly string[], names: Record<string, string>, opts: { required?: boolean; empty?: string; initial?: string } = {}) => (
    <Field id={name} label={label} error={fieldError(name)} requiredMark={opts.required ? form.requiredMark : undefined}>
      {(describedBy, invalid) => (
        <select id={name} name={name} defaultValue={v[name] ?? opts.initial ?? ""} required={opts.required} aria-describedby={describedBy} aria-invalid={invalid}>
          {opts.empty !== undefined && <option value="">{opts.empty || form.choose}</option>}
          {options.map((o) => (
            <option key={o} value={o}>
              {names[o] ?? o}
            </option>
          ))}
        </select>
      )}
    </Field>
  );

  return (
    <form ref={formRef} action={action} className="form big" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <ErrorSummary state={state} text={form} errors={errors} summaryRef={summaryRef} />

      <fieldset className="card">
        <legend>{t.step1}</legend>
        {text("district", true)}
        {text("mandal", true)}
        {text("village", true)}
        {text("surveyNumber", true)}
        {text("lgdVillageCode", false, "numeric")}
      </fieldset>

      <fieldset className="card">
        <legend>{t.step2}</legend>
        {select("landClass", t.landClass, LAND_CLASSES, t.landClasses, { required: true, empty: "" })}
        <div className="row">
          {text("areaValue", true, "decimal")}
          {select("areaUnit", t.areaUnit, AREA_UNITS, unitNames, { required: true, initial: "square_yard" })}
        </div>
        {select("areaSource", t.areaSource, AREA_SOURCES, t.areaSources, { required: true, initial: "lister" })}
        {select("lengthUnit", t.lengthUnit, LENGTH_UNITS, t.lengthUnits, { initial: "feet" })}
        <div className="row">
          {text("frontage", false, "decimal")}
          {text("depth", false, "decimal")}
        </div>
        {select("facing", t.facing, FACINGS, t.facings, { empty: t.notSure })}
      </fieldset>

      <fieldset className="card">
        <legend>{t.step3}</legend>
        {text("roadWidth", false, "decimal")}
        {select("roadWidthSource", t.roadWidthSource, ROAD_WIDTH_SOURCES, t.roadWidthSources, { empty: t.notSure })}
        {select("roadType", t.roadType, ROAD_TYPES, t.roadTypes, { empty: t.notSure })}
        {select("accessRight", t.accessRight, ACCESS_RIGHTS, t.accessRights, { empty: t.notSure })}
        {text("distanceToMainRoadM", false, "numeric")}
      </fieldset>

      <fieldset className="card">
        <legend>{t.step4}</legend>
        <p className="hint">{t.afterSubmit}</p>
        <button type="submit" disabled={pending}>
          {pending ? form.sending : t.submit}
        </button>
      </fieldset>
    </form>
  );
}
