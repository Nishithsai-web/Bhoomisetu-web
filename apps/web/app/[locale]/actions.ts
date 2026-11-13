"use server";

import { redirect } from "next/navigation";
import {
  activateSubscription,
  createDevelopmentProject,
  decideReport,
  linkDevelopment,
  reportListing,
  setParcelPin,
  createRental,
  getRentalContact,
  setRentalStatus,
  createOffer,
  createParcel,
  decideChangeRequest,
  decideDocument,
  findUserByPhone,
  getContact,
  getParcelPlace,
  isUuid,
  mergeDuplicate,
  removeDocument,
  requestChange,
  requestOtp,
  setListerConfirmation,
  setOwnerOfRecord,
  setWhatsapp,
  signOut,
  updateMyOffer,
  uploadDocument,
  verifyOtp,
  type MyOfferAction,
} from "@/lib/api";
import { CHANGE_FIELDS, parseOfferForm, parseParcelForm, safeNextPath, toMetres, type FieldError } from "@/lib/forms";
import { whatsappLink } from "@/lib/contact";
import { getDictionary, type Locale } from "@/lib/i18n";
import { parseAreaValue, toAsciiDigits } from "@/lib/units";
import { isLocale } from "@/lib/i18n";
import { clearSession, getSessionToken, setSession } from "@/lib/session";

/** What a form shows after a submit: field errors, a form-level error code, or the next step. */
export type FormState = {
  errors?: FieldError[];
  error?: string;
  step?: "phone" | "code";
  phone?: string;
  values?: Record<string, string>;
};

const value = (form: FormData, name: string) => {
  const v = form.get(name);
  return typeof v === "string" ? v.trim() : "";
};

/** Keeps what the person typed, so a mistake never makes them fill the whole form again. */
const echo = (form: FormData): Record<string, string> => {
  const out: Record<string, string> = {};
  for (const [key, v] of form.entries()) {
    if (typeof v === "string" && !key.startsWith("$")) out[key] = v;
  }
  return out;
};

const localeOf = (form: FormData) => {
  const locale = value(form, "locale");
  return isLocale(locale) ? locale : "en";
};

/**
 * One action for both sign-in steps, so the screen always follows the latest server answer:
 * intent "request" sends a code and moves to the code step; intent "verify" checks it and signs in.
 */
export async function signInAction(previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const phone = value(form, "phone");

  if (value(form, "intent") !== "verify") {
    // Cloudflare Turnstile puts its token in this field when the widget is shown (BHOOMISETU_TURNSTILE_SITE_KEY).
    const sent = await requestOtp(phone, value(form, "cf-turnstile-response"));
    if (sent.ok) return { step: "code", phone };
    // "Send a new code" pressed too early: stay on the code step, the earlier code still works.
    const stay = previous.step === "code" && sent.code === "otp.too_soon";
    return { step: stay ? "code" : "phone", phone, error: sent.code };
  }

  // People paste codes as "123 456"; spaces and dashes are not part of the code.
  const result = await verifyOtp(phone, value(form, "code").replace(/[\s-]/g, ""));
  if (!result.ok) {
    // A finished or locked code needs a fresh one, so send the person back to the phone step.
    const restart = ["otp.expired", "otp.no_active_code", "otp.too_many_attempts"].includes(result.code);
    return { step: restart ? "phone" : "code", phone, error: result.code };
  }
  await setSession(result.data.accessToken, result.data.expiresAt);
  redirect(safeNextPath(value(form, "next"), `/${locale}`));
}

export async function signOutAction(form: FormData): Promise<void> {
  const token = await getSessionToken();
  // End the session on the server too; the cookie is cleared even if the API cannot be reached.
  if (token) await signOut(token);
  await clearSession();
  redirect(`/${localeOf(form)}`);
}

export async function createParcelAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/sell`);

  const parsed = parseParcelForm(form);
  if (!parsed.ok) return { errors: parsed.errors, values: echo(form) };

  const result = await createParcel(parsed.value, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/sell`);
    }
    if (result.code === "parcel.already_exists" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(`/${locale}/parcels/${result.existingParcelId}/offer?existing=1`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/parcels/${result.data.id}/offer?new=1`);
}

export async function createOfferAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };

  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/offer`);

  const parsed = parseOfferForm(form);
  if (!parsed.ok) return { errors: parsed.errors, values: echo(form) };

  const result = await createOffer(parcelId, parsed.value, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/offer`);
    }
    // An old link to a record set aside as a duplicate: send the person to the record everyone sees.
    if (result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(`/${locale}/parcels/${result.existingParcelId}/offer?existing=1`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/parcels/${parcelId}/compare`);
}

const MY_OFFER_ACTIONS: readonly MyOfferAction[] = ["still-available", "sold", "withdraw"];

/** One-tap updates on the My listings page. Plain form posts, so they work even before JavaScript loads. */
export async function myOfferAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const action = value(form, "action") as MyOfferAction;
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/mine`);
  if (!isUuid(parcelId) || !MY_OFFER_ACTIONS.includes(action)) redirect(`/${locale}/mine?error=api.error`);

  const result = await updateMyOffer(parcelId, action, token);
  if (!result.ok && result.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/mine`);
  }
  redirect(result.ok ? `/${locale}/mine?done=${action}` : `/${locale}/mine?error=${encodeURIComponent(result.code)}`);
}

export async function changeRequestAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/correct`);

  const field = value(form, "field");
  const reason = value(form, "reason");
  let proposed = value(form, "proposedValue");
  const errors: FieldError[] = [];
  if (!(CHANGE_FIELDS as readonly string[]).includes(field)) errors.push({ field: "field", code: "form.required" });
  if (proposed === "") errors.push({ field: "proposedValue", code: "form.required" });
  if (reason.length < 3) errors.push({ field: "reason", code: "change.reason_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  // Lengths may be typed in feet; the API stores metres.
  if (["frontage_m", "depth_m", "road_width_m"].includes(field) && value(form, "lengthUnit") === "feet") {
    const feet = parseAreaValue(proposed); // positive number; Telugu and Devanagari digits accepted
    if (feet === null) return { errors: [{ field: "proposedValue", code: "form.number_invalid" }], values: echo(form) };
    proposed = String(toMetres(feet, "feet"));
  }

  const result = await requestChange(parcelId, { field, proposedValue: proposed, reason }, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=/${locale}/parcels/${parcelId}/correct`);
    }
    if (result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(`/${locale}/parcels/${result.existingParcelId}/correct`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/mine?done=change-requested`);
}

/** Sends people whose session ended back to sign in, then to the page they were on. */
async function requireToken(locale: string, back: string): Promise<string> {
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=${encodeURIComponent(back)}`);
  return token;
}

/** The owner of record confirms, or removes confirmation from, whoever made an offer. */
export async function ownerConfirmAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const offerId = value(form, "offerId");
  if (!isUuid(parcelId) || !isUuid(offerId)) redirect(`/${locale}`);
  const back = `/${locale}/parcels/${parcelId}/compare`;
  const token = await requireToken(locale, back);
  const result = await setListerConfirmation(parcelId, offerId, value(form, "intent") === "confirm", token);
  redirect(result.ok ? `${back}?done=owner` : `${back}?error=${encodeURIComponent(result.code)}`);
}

// ---------------------------------------------------------------- reviewer console

const REVIEW = (locale: string) => `/${locale}/review`;

export async function decideChangeAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const requestId = value(form, "requestId");
  const decision = value(form, "decision");
  if (!isUuid(requestId) || (decision !== "approve" && decision !== "reject")) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await decideChangeRequest(requestId, decision, value(form, "note").slice(0, 1000), token);
  redirect(result.ok ? `${REVIEW(locale)}?done=decided` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

export async function mergeDuplicateAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await mergeDuplicate(parcelId, token);
  redirect(result.ok ? `${REVIEW(locale)}?done=merged` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

/** Finds the account by mobile number, then records it as owner of record, in one step for the reviewer. */
export async function setOwnerAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const phone = value(form, "phone");
  const evidence = value(form, "evidence");
  const errors: FieldError[] = [];
  if (!isUuid(parcelId)) errors.push({ field: "parcelId", code: "review.parcel_id_invalid" });
  if (phone === "") errors.push({ field: "phone", code: "phone.empty" });
  if (evidence.length < 3) errors.push({ field: "evidence", code: "admin.evidence_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  const token = await requireToken(locale, REVIEW(locale));
  const user = await findUserByPhone(phone, token);
  if (!user.ok) return { error: user.status === 404 ? "review.no_account" : user.code, values: echo(form) };
  const result = await setOwnerOfRecord(parcelId, user.data.id, evidence, token);
  if (!result.ok) return { error: result.code, values: echo(form) };
  redirect(`${REVIEW(locale)}?done=owner_set`);
}

// ---------------------------------------------------------------- documents, contact, subscriptions

const DOCS = (locale: string, parcelId: string) => `/${locale}/parcels/${parcelId}/documents`;

/** Lister adds one public document. The file goes to the API as it is; the API checks type, size and content. */
export async function uploadDocumentAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };
  const token = await requireToken(locale, DOCS(locale, parcelId));

  const type = value(form, "type");
  const file = form.get("file");
  const errors: FieldError[] = [];
  if (type === "") errors.push({ field: "type", code: "document.type_invalid" });
  if (!(file instanceof File) || file.size === 0) errors.push({ field: "file", code: "document.file_required" });
  else if (file.size > 10 * 1024 * 1024) errors.push({ field: "file", code: "document.too_large" });
  if (form.get("declaration") !== "on") errors.push({ field: "declaration", code: "document.declaration_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  const body = new FormData();
  body.set("type", type);
  body.set("referenceNumber", value(form, "referenceNumber"));
  body.set("declaration", "true");
  body.set("file", file as File);
  const result = await uploadDocument(parcelId, body, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=${encodeURIComponent(DOCS(locale, parcelId))}`);
    }
    if (result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(DOCS(locale, result.existingParcelId));
    }
    const field = result.code.startsWith("document.reference") ? "referenceNumber" : result.code === "document.type_invalid" ? "type" : null;
    return field ? { errors: [{ field, code: result.code }], values: echo(form) } : { error: result.code, values: echo(form) };
  }
  redirect(`${DOCS(locale, parcelId)}?done=uploaded`);
}

export async function removeDocumentAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const documentId = value(form, "documentId");
  if (!isUuid(parcelId) || !isUuid(documentId)) redirect(`/${locale}/parcels`);
  const token = await requireToken(locale, DOCS(locale, parcelId));
  const result = await removeDocument(parcelId, documentId, token);
  redirect(result.ok ? `${DOCS(locale, parcelId)}?done=removed` : `${DOCS(locale, parcelId)}?error=${encodeURIComponent(result.code)}`);
}

/** "Chat on WhatsApp": asks the API for the lister's number (signed in only), then opens WhatsApp with a first message. */
export async function contactAction(form: FormData): Promise<void> {
  const locale = localeOf(form) as Locale;
  const parcelId = value(form, "parcelId");
  const offerId = value(form, "offerId");
  if (!isUuid(parcelId) || !isUuid(offerId)) redirect(`/${locale}/parcels`);
  const back = `/${locale}/parcels/${parcelId}/compare`;
  const token = await requireToken(locale, back);
  const result = await getContact(parcelId, offerId, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=${encodeURIComponent(back)}`);
    }
    redirect(`${back}?error=${encodeURIComponent(result.code)}`);
  }
  // The place in the first message comes from the land record, never from the submitted form.
  const t = getDictionary(locale);
  const parcel = await getParcelPlace(parcelId);
  const place = parcel.ok ? `${parcel.data.location.village}, ${parcel.data.location.mandal} (${parcel.data.location.surveyNumber})` : "";
  const message = t.contact.firstMessage.replace("{place}", place);
  const link = whatsappLink(result.data.whatsappE164, message);
  redirect(link ?? `${back}?error=api.error`);
}

export async function setWhatsappAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const token = await requireToken(locale, `/${locale}/mine`);
  const result = await setWhatsapp(value(form, "whatsapp"), token);
  redirect(result.ok ? `/${locale}/mine?done=whatsapp` : `/${locale}/mine?error=${encodeURIComponent(result.code)}`);
}

export async function decideDocumentAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const documentId = value(form, "documentId");
  const decision = value(form, "decision");
  if (!isUuid(documentId) || (decision !== "approve" && decision !== "reject")) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await decideDocument(documentId, decision, value(form, "note"), token);
  redirect(result.ok ? `${REVIEW(locale)}?done=document` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

/** Reviewer records a subscription after the payment has been received outside the app. */
export async function activateSubscriptionAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const phone = value(form, "phone");
  const audience = value(form, "audience");
  const months = Number(toAsciiDigits(value(form, "months")));
  const amount = parseRupeesLoose(value(form, "amount"));
  const reference = value(form, "paymentReference");
  const errors: FieldError[] = [];
  if (phone === "") errors.push({ field: "phone", code: "phone.empty" });
  if (audience !== "lister" && audience !== "viewer") errors.push({ field: "audience", code: "subscription.audience_invalid" });
  if (!Number.isInteger(months) || months < 1 || months > 24) errors.push({ field: "months", code: "subscription.months_invalid" });
  if (amount === null) errors.push({ field: "amount", code: "subscription.amount_invalid" });
  if (reference === "") errors.push({ field: "paymentReference", code: "subscription.reference_required" });
  if (errors.length > 0) return { errors, values: echo(form) };

  const token = await requireToken(locale, REVIEW(locale));
  const result = await activateSubscription(
    { phone, audience: audience as "lister" | "viewer", months, amountRupees: amount as number, paymentReference: reference },
    token,
  );
  if (!result.ok) return { error: result.status === 404 ? "review.no_account" : result.code, values: echo(form) };
  redirect(`${REVIEW(locale)}?done=subscription`);
}

/** Amount paid in rupees: digits with optional commas and up to two decimals; 0 is allowed (free plans). */
function parseRupeesLoose(raw: string): number | null {
  const cleaned = raw.replace(/[,\s₹]/g, "");
  if (!/^\d{1,8}(\.\d{1,2})?$/.test(cleaned)) return null;
  return Number(cleaned);
}

// ---------------------------------------------------------------- reports and nearby developments

/** Anyone signed in reports a problem with a listing; a reviewer handles it. */
export async function reportListingAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) return { error: "not_found" };
  const here = `/${locale}/parcels/${parcelId}/report`;
  const reason = value(form, "reason");
  const details = value(form, "details");
  if (reason === "") return { errors: [{ field: "reason", code: "report.reason_invalid" }], values: echo(form) };
  if (reason === "other" && details === "") return { errors: [{ field: "details", code: "report.details_required" }], values: echo(form) };
  const token = await requireToken(locale, here);
  const result = await reportListing(parcelId, { reason, details }, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=${encodeURIComponent(here)}`);
    }
    if (result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
      redirect(`/${locale}/parcels/${result.existingParcelId}/report`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/parcels/${parcelId}?done=reported`);
}

export async function decideReportAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const reportId = value(form, "reportId");
  const decision = value(form, "decision");
  if (!isUuid(reportId) || (decision !== "resolve" && decision !== "dismiss")) redirect(REVIEW(locale));
  const token = await requireToken(locale, REVIEW(locale));
  const result = await decideReport(reportId, decision, value(form, "note"), token);
  redirect(result.ok ? `${REVIEW(locale)}?done=report` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}`);
}

export async function createDevelopmentAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const token = await requireToken(locale, REVIEW(locale));
  const sourceUrl = value(form, "sourceUrl");
  const result = await createDevelopmentProject(
    { name: value(form, "name"), kind: value(form, "kind"), status: value(form, "status"), authority: value(form, "authority"), sourceUrl: sourceUrl || null, sourceDate: value(form, "sourceDate") },
    token,
  );
  redirect(result.ok ? `${REVIEW(locale)}?done=development#developments` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}#developments`);
}

export async function linkDevelopmentAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  const projectId = value(form, "projectId");
  if (!isUuid(parcelId)) redirect(`${REVIEW(locale)}?error=review.parcel_id_invalid#developments`);
  if (!isUuid(projectId)) redirect(`${REVIEW(locale)}?error=development.project_invalid#developments`);
  const rawDistance = value(form, "distanceKm");
  const distance = rawDistance === "" ? null : Number(toAsciiDigits(rawDistance));
  if (distance !== null && !Number.isFinite(distance)) redirect(`${REVIEW(locale)}?error=development.distance_invalid#developments`);
  const token = await requireToken(locale, REVIEW(locale));
  const result = await linkDevelopment(parcelId, { projectId, distanceKm: distance, mayAffectParcel: form.get("mayAffectParcel") === "on" }, token);
  redirect(result.ok ? `${REVIEW(locale)}?done=linked#developments` : `${REVIEW(locale)}?error=${encodeURIComponent(result.code)}#developments`);
}

/** Sets the land's map pin from the phone's location or typed coordinates. */
export async function setPinAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const parcelId = value(form, "parcelId");
  if (!isUuid(parcelId)) redirect(`/${locale}/parcels`);
  const back = `/${locale}/parcels/${parcelId}`;
  // Telugu and Devanagari digits are accepted here too, as on every other form.
  const latitude = Number(toAsciiDigits(value(form, "latitude")).replace(",", "."));
  const longitude = Number(toAsciiDigits(value(form, "longitude")).replace(",", "."));
  const source = value(form, "source") === "gps" ? "gps" : "map";
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude) || value(form, "latitude") === "" || value(form, "longitude") === "") {
    redirect(`${back}?error=pin.outside_india#map`);
  }
  const token = await requireToken(locale, back);
  const result = await setParcelPin(parcelId, { latitude, longitude, source }, token);
  redirect(result.ok ? `${back}?done=pin#map` : `${back}?error=${encodeURIComponent(result.code)}#map`);
}

// ---------------------------------------------------------------- homes for rent

/** Lists a home for rent. The browser sends the map point (phone location on the spot, or typed numbers). */
export async function createRentalAction(_previous: FormState, form: FormData): Promise<FormState> {
  const locale = localeOf(form);
  const here = `/${locale}/rent/new`;
  // Telugu and Devanagari digits are accepted; amounts may have commas; coordinates may use a decimal comma.
  const number = (name: string) => {
    const raw = toAsciiDigits(value(form, name)).replace(/[\s₹]/g, "");
    const cleaned = name === "latitude" || name === "longitude" ? raw.replace(",", ".") : raw.replace(/,/g, "");
    return cleaned === "" ? Number.NaN : Number(cleaned);
  };
  const body = {
    listerKind: value(form, "listerKind"),
    propertyType: value(form, "propertyType"),
    bedrooms: number("bedrooms"),
    areaSqft: number("areaSqft"),
    rentRupees: number("rentRupees"),
    depositRupees: number("depositRupees"),
    furnishing: value(form, "furnishing"),
    preferredTenants: value(form, "preferredTenants"),
    parking: form.get("parking") === "on",
    availableFrom: value(form, "availableFrom"),
    locality: value(form, "locality"),
    mandal: value(form, "mandal"),
    district: value(form, "district"),
    latitude: number("latitude"),
    longitude: number("longitude"),
  };
  const errors: FieldError[] = [];
  const need = (field: string, ok: boolean, code: string) => {
    if (!ok) errors.push({ field, code });
  };
  need("listerKind", body.listerKind === "owner" || body.listerKind === "agent", "rental.lister_kind_invalid");
  need("propertyType", body.propertyType !== "", "rental.type_invalid");
  need("bedrooms", Number.isInteger(body.bedrooms) && body.bedrooms >= 0 && body.bedrooms <= 10, "rental.bedrooms_invalid");
  need("areaSqft", Number.isInteger(body.areaSqft) && body.areaSqft >= 50 && body.areaSqft <= 20000, "rental.area_invalid");
  need("rentRupees", Number.isInteger(body.rentRupees) && body.rentRupees >= 500, "rental.rent_invalid");
  need("depositRupees", Number.isInteger(body.depositRupees) && body.depositRupees >= 0, "rental.deposit_invalid");
  need("furnishing", body.furnishing !== "", "rental.furnishing_invalid");
  need("preferredTenants", body.preferredTenants !== "", "rental.tenants_invalid");
  need("availableFrom", /^\d{4}-\d{2}-\d{2}$/.test(body.availableFrom), "rental.available_invalid");
  need("locality", body.locality.length >= 2, "rental.place_invalid");
  need("mandal", body.mandal.length >= 2, "rental.place_invalid");
  need("district", body.district.length >= 2, "rental.place_invalid");
  need("latitude", Number.isFinite(body.latitude) && Number.isFinite(body.longitude), "pin.outside_india");
  if (errors.length > 0) return { errors, values: echo(form) };

  const token = await requireToken(locale, here);
  const result = await createRental(body, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=${encodeURIComponent(here)}`);
    }
    return { error: result.code, values: echo(form) };
  }
  redirect(`/${locale}/rentals/${result.data.id}?done=listed`);
}

export async function rentalStatusAction(form: FormData): Promise<void> {
  const locale = localeOf(form);
  const id = value(form, "rentalId");
  const action = value(form, "action");
  if (!isUuid(id) || !["still_available", "rented", "withdraw", "relist"].includes(action)) redirect(`/${locale}/mine`);
  const token = await requireToken(locale, `/${locale}/mine`);
  const result = await setRentalStatus(id, action as "still_available" | "rented" | "withdraw" | "relist", token);
  redirect(result.ok ? `/${locale}/mine?done=rental#rentals` : `/${locale}/mine?error=${encodeURIComponent(result.code)}#rentals`);
}

export async function contactRentalAction(form: FormData): Promise<void> {
  const locale = localeOf(form) as Locale;
  const id = value(form, "rentalId");
  if (!isUuid(id)) redirect(`/${locale}/map?mode=rent`);
  const back = `/${locale}/rentals/${id}`;
  const token = await requireToken(locale, back);
  const result = await getRentalContact(id, token);
  if (!result.ok) {
    if (result.status === 401) {
      await clearSession();
      redirect(`/${locale}/signin?next=${encodeURIComponent(back)}`);
    }
    redirect(`${back}?error=${encodeURIComponent(result.code)}`);
  }
  const t = getDictionary(locale);
  const link = whatsappLink(result.data.whatsappE164, t.rent.firstMessage);
  redirect(link ?? `${back}?error=api.error`);
}
