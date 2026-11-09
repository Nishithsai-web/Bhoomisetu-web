"use client";

import { useActionState, useState } from "react";
import { createRentalAction, type FormState } from "@/app/[locale]/actions";
import type { Dictionary } from "@/lib/i18n";
import { Field } from "./Field";
import { ErrorSummary, useFormFeedback } from "./FormFeedback";

type Options = { propertyTypes: readonly string[]; furnishings: readonly string[]; tenants: readonly string[] };

/** List a home for rent: the basics a renter compares, and where it is (phone location at the home, or typed). */
export function RentalForm({ locale, t, options }: { locale: string; t: Dictionary; options: Options }) {
  const [state, action, pending] = useActionState<FormState, FormData>(createRentalAction, {});
  const { formRef, summaryRef } = useFormFeedback(state);
  const r = t.rent;
  const errors = t.errors as Record<string, string>;
  const v = state.values ?? {};
  const [latitude, setLatitude] = useState(v.latitude ?? "");
  const [longitude, setLongitude] = useState(v.longitude ?? "");
  const [atHome, setAtHome] = useState(false);
  const [status, setStatus] = useState("");
  const fieldError = (name: string) => {
    const code = state.errors?.find((e) => e.field === name)?.code;
    return code ? (Object.hasOwn(errors, code) ? errors[code] : errors.generic) : undefined;
  };
  const lat = Number(latitude);
  const lng = Number(longitude);
  const preview = latitude !== "" && longitude !== "" && lat >= 6 && lat <= 37.6 && lng >= 68 && lng <= 97.5 ? { lat, lng } : null;

  const locate = () => {
    if (!("geolocation" in navigator)) return setStatus(t.map.form.unavailable);
    setStatus(t.map.form.locating);
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setLatitude(p.coords.latitude.toFixed(4));
        setLongitude(p.coords.longitude.toFixed(4));
        setStatus("");
      },
      (e) => setStatus(e.code === e.PERMISSION_DENIED ? t.map.form.denied : t.map.form.unavailable),
      { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 },
    );
  };

  const select = (name: string, label: string, values: readonly string[], labels: Record<string, string>) => (
    <Field id={name} label={label} error={fieldError(name)} requiredMark={t.form.requiredMark}>
      {(describedBy, invalid) => (
        <select id={name} name={name} defaultValue={v[name] ?? ""} aria-describedby={describedBy} aria-invalid={invalid}>
          <option value="">{t.form.choose}</option>
          {values.map((x) => (
            <option key={x} value={x}>
              {labels[x] ?? x}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
  const text = (name: string, label: string, mode: "numeric" | "text" = "text", hint?: string) => (
    <Field id={name} label={label} hint={hint} error={fieldError(name)} requiredMark={t.form.requiredMark}>
      {(describedBy, invalid) => <input id={name} name={name} inputMode={mode} defaultValue={v[name] ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />}
    </Field>
  );

  return (
    <form ref={formRef} action={action} className="card form" noValidate>
      <input type="hidden" name="locale" value={locale} />
      <ErrorSummary state={state} text={t.form} errors={errors} summaryRef={summaryRef} />
      {select("listerKind", r.youAre, ["owner", "agent"], { owner: r.owner, agent: r.agent })}
      {select("propertyType", r.typeLabel, options.propertyTypes, r.types as Record<string, string>)}
      {text("bedrooms", r.bedrooms, "numeric", r.bedroomsHint)}
      {text("areaSqft", r.area, "numeric")}
      {text("rentRupees", r.rent, "numeric")}
      {text("depositRupees", r.deposit, "numeric")}
      {select("furnishing", r.furnishing, options.furnishings, r.furnishings as Record<string, string>)}
      {select("preferredTenants", r.tenants, options.tenants, r.tenantKinds as Record<string, string>)}
      <label className="choice">
        <input type="checkbox" name="parking" defaultChecked={v.parking === "on"} /> {r.parking}
      </label>
      <Field id="availableFrom" label={r.availableFrom} error={fieldError("availableFrom")} requiredMark={t.form.requiredMark}>
        {(describedBy, invalid) => <input id="availableFrom" name="availableFrom" type="date" defaultValue={v.availableFrom ?? ""} aria-describedby={describedBy} aria-invalid={invalid} />}
      </Field>
      {text("locality", r.locality)}
      {text("mandal", t.sell.mandal)}
      {text("district", t.sell.district)}

      <fieldset>
        <legend>{r.whereTitle}</legend>
        <p className="hint">{r.whereHelp}</p>
        <label className="choice">
          <input type="checkbox" checked={atHome} onChange={(e) => setAtHome(e.target.checked)} /> {r.atHome}
        </label>
        <button type="button" className="secondary" onClick={locate} disabled={!atHome}>
          {t.map.form.useLocation}
        </button>
        <p role="status" className="hint">
          {status}
        </p>
        <Field id="latitude" label={t.map.form.latitude} error={fieldError("latitude")}>
          {(describedBy, invalid) => (
            <input id="latitude" name="latitude" inputMode="decimal" value={latitude} onChange={(e) => setLatitude(e.target.value)} aria-describedby={describedBy} aria-invalid={invalid} />
          )}
        </Field>
        <div className="field">
          <label htmlFor="longitude">{t.map.form.longitude}</label>
          <input id="longitude" name="longitude" inputMode="decimal" value={longitude} onChange={(e) => setLongitude(e.target.value)} />
        </div>
        {preview && (
          <>
            <p className="hint">{t.map.form.preview}</p>
            <iframe
              className="map-frame"
              title={t.map.form.preview}
              loading="lazy"
              referrerPolicy="no-referrer"
              src={`https://www.openstreetmap.org/export/embed.html?bbox=${preview.lng - 0.005},${preview.lat - 0.003},${preview.lng + 0.005},${preview.lat + 0.003}&layer=mapnik&marker=${preview.lat},${preview.lng}`}
            />
          </>
        )}
      </fieldset>
      <button type="submit" disabled={pending}>
        {pending ? t.form.sending : r.submit}
      </button>
    </form>
  );
}
