// Server-side client for the Bhoomisetu API. Types mirror the API's JSON (camelCase).

export type Sort = "best_value" | "lowest_price" | "best_verified" | "fastest_response";
export const SORTS: readonly Sort[] = ["best_value", "lowest_price", "best_verified", "fastest_response"];

export type Badge =
  | "best_value"
  | "lowest_price"
  | "verify_before_visiting"
  | "no_brokerage"
  | "availability_stale"
  | "authority_not_confirmed";

export type ComparedOffer = {
  rank: number;
  offerId: string;
  /** The lister's chosen display name, if any; otherwise show "Lister {listerNumber}" in the reader's language. */
  listerName: string | null;
  listerNumber: number;
  listerKind: "owner" | "agent";
  /** Says "owner" but is not yet the owner of record; priced and badged like an agent. */
  ownerClaimUnverified: boolean;
  authority: "owner_of_record" | "owner_confirmed_agent" | "not_confirmed";
  checks: ("phone_otp" | "identity" | "rera_agent")[];
  askingPriceRupees: number;
  buyerBrokeragePercent: number | null;
  brokerageAssumed: boolean;
  effectivePriceRupees: number;
  effectivePricePerSquareYardRupees: number;
  effectivePricePerAcreRupees: number;
  medianResponseMinutes: number | null;
  availabilityConfirmedAt: string;
  score: number;
  breakdown: { price: number; authority: number; checks: number; response: number; freshness: number };
  badges: Badge[];
};

export type Comparison = {
  parcelId: string;
  landClass: "agricultural" | "non_agricultural" | "converted" | "unknown";
  areaSquareYards: number;
  areaAcres: number;
  sort: Sort;
  medianEffectivePriceRupees: number | null;
  rules: {
    priceWeight: number;
    authorityWeight: number;
    checksWeight: number;
    responseWeight: number;
    freshnessWeight: number;
    lowPriceOutlierRatio: number;
    assumedUndisclosedBrokeragePercent: number;
    staleAfterDays: number;
  };
  offers: ComparedOffer[];
};

export type ParcelSummary = {
  id: string;
  stateCode: string;
  district: string;
  mandal: string;
  village: string;
  surveyNumber: string;
  landClass: string;
  areaSquareYards: number;
  activeOffers: number;
};

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number };

export type ApiProblem = {
  ok: false;
  status: number;
  code: string;
  /** Present on 409 parcel.already_exists and parcel.duplicate: the record to use instead. */
  existingParcelId?: string;
  /** Present on 429 OTP responses. */
  retryAfterSeconds?: number;
};

export type ApiResult<T> = { ok: true; data: T } | ApiProblem;

export function isSort(value: unknown): value is Sort {
  return typeof value === "string" && (SORTS as readonly string[]).includes(value);
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isUuid(value: string): boolean {
  return UUID.test(value);
}

function baseUrl(): string {
  const url = process.env.BHOOMISETU_API_URL;
  if (!url) {
    throw new Error("BHOOMISETU_API_URL is not set.");
  }
  return url.replace(/\/+$/, "");
}

/**
 * The visitor's IP, forwarded so the API's per-IP limits apply to each visitor rather than to the web server.
 *
 * Only trustworthy when this server is reachable solely through a load balancer that appends the real client
 * address as the last X-Forwarded-For entry (earlier entries can be typed by anyone). That must be declared
 * with BHOOMISETU_BEHIND_LOAD_BALANCER=true; otherwise nothing is forwarded and the API limits the web server
 * as one client. See docs/ARCHITECTURE.md, "Security model".
 */
async function visitorIp(): Promise<string | null> {
  if (process.env.BHOOMISETU_BEHIND_LOAD_BALANCER !== "true") return null;
  try {
    const { headers } = await import("next/headers");
    const forwarded = (await headers()).get("x-forwarded-for");
    const last = forwarded?.split(",").map((part: string) => part.trim()).filter(Boolean).at(-1);
    return last && /^[0-9a-fA-F:.]{2,45}$/.test(last) ? last : null;
  } catch {
    // Outside a request (for example at build time) there is no visitor.
    return null;
  }
}

async function request<T>(method: "GET" | "POST" | "PUT" | "DELETE", path: string, body?: unknown, token?: string): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { accept: "application/json" };
  const ip = await visitorIp();
  if (ip) headers["x-forwarded-for"] = ip;
  // FormData (file uploads) sets its own multipart content type; everything else is JSON.
  const isForm = body instanceof FormData;
  if (body !== undefined && !isForm) headers["content-type"] = "application/json";
  if (token) headers.authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${baseUrl()}${path}`, {
      method,
      cache: "no-store",
      headers,
      body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
      signal: AbortSignal.timeout(isForm ? 60000 : 8000),
    });
  } catch (error) {
    console.error(`API request failed: ${method} ${path}`, error);
    return { ok: false, status: 503, code: "api.unreachable" };
  }

  if (!response.ok) {
    const problem: ApiProblem = { ok: false, status: response.status, code: response.status === 401 ? "auth.required" : "api.error" };
    try {
      const json = (await response.json()) as { code?: unknown; existingParcelId?: unknown; retryAfterSeconds?: unknown };
      if (typeof json.code === "string") problem.code = json.code;
      if (typeof json.existingParcelId === "string") problem.existingParcelId = json.existingParcelId;
      if (typeof json.retryAfterSeconds === "number") problem.retryAfterSeconds = json.retryAfterSeconds;
    } catch {
      // Body was not JSON (for example a bare 401); keep the status-based code.
    }
    return problem;
  }

  if (response.status === 202 || response.status === 204) {
    return { ok: true, data: undefined as T };
  }
  return { ok: true, data: (await response.json()) as T };
}

const get = <T,>(path: string, token?: string) => request<T>("GET", path, undefined, token);

export function getComparison(parcelId: string, sort: Sort): Promise<ApiResult<Comparison>> {
  return get<Comparison>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/comparison?sort=${sort}`);
}

/** Free-text search over village, mandal and district names. */
export function searchParcels(query: string, page: number): Promise<ApiResult<Paged<ParcelSummary>>> {
  const params = new URLSearchParams({ page: String(page), pageSize: "20" });
  if (query) params.set("q", query);
  return get<Paged<ParcelSummary>>(`/api/v1/parcels?${params.toString()}`);
}

export type SignIn = { accessToken: string; expiresAt: string; userId: string };

export function requestOtp(phone: string, captchaToken?: string): Promise<ApiResult<void>> {
  return request<void>("POST", "/api/v1/auth/otp/request", { phone, captchaToken: captchaToken || undefined });
}

export function verifyOtp(phone: string, code: string): Promise<ApiResult<SignIn>> {
  return request<SignIn>("POST", "/api/v1/auth/otp/verify", { phone, code });
}

export type Me = { id: string; phoneMasked: string; displayName: string | null; preferredLocale: string; checks: string[]; isTrustReviewer: boolean };

export function getMe(token: string): Promise<ApiResult<Me>> {
  return get<Me>("/api/v1/me", token);
}

export type CreateParcel = {
  stateCode: string;
  district: string;
  mandal: string;
  village: string;
  lgdVillageCode: string | null;
  surveyNumber: string;
  landClass: string;
  area: { value: number; unit: string };
  areaSource: string;
  frontageM: number | null;
  depthM: number | null;
  facing: string | null;
  road: { widthM: number | null; widthSource: string | null; type: string | null; accessRight: string | null; distanceToMainRoadM: number | null } | null;
};

export function createParcel(body: CreateParcel, token: string): Promise<ApiResult<{ id: string }>> {
  return request<{ id: string }>("POST", "/api/v1/parcels", body, token);
}

export type CreateOffer = { listerKind: "owner" | "agent"; askingPriceRupees: number; buyerBrokeragePercent: number | null; brokerageTerms: string | null };

export function createOffer(parcelId: string, body: CreateOffer, token: string): Promise<ApiResult<{ id: string }>> {
  return request<{ id: string }>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/offers`, body, token);
}

export type MyOffer = {
  offerId: string;
  parcelId: string;
  village: string;
  mandal: string;
  district: string;
  surveyNumber: string;
  areaSquareYards: number;
  listerKind: "owner" | "agent";
  askingPriceRupees: number;
  buyerBrokeragePercent: number | null;
  status: "active" | "withdrawn" | "sold";
  availabilityConfirmedAt: string;
  daysSinceConfirmed: number;
  needsConfirmation: boolean;
};

export function getMyOffers(token: string): Promise<ApiResult<MyOffer[]>> {
  return get<MyOffer[]>("/api/v1/me/offers", token);
}

export type MyOfferAction = "still-available" | "sold" | "withdraw";

export function updateMyOffer(parcelId: string, action: MyOfferAction, token: string): Promise<ApiResult<void>> {
  return request<void>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/offers/mine/${action}`, undefined, token);
}

export type ChangeRequest = {
  id: string;
  parcelId: string;
  field: string;
  currentValue: string | null;
  proposedValue: string;
  reason: string;
  status: "pending" | "approved" | "rejected";
  createdAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
};

export function requestChange(parcelId: string, body: { field: string; proposedValue: string; reason: string }, token: string): Promise<ApiResult<ChangeRequest>> {
  return request<ChangeRequest>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/change-requests`, body, token);
}

export function getMyChangeRequests(token: string): Promise<ApiResult<ChangeRequest[]>> {
  return get<ChangeRequest[]>("/api/v1/me/change-requests", token);
}

/** Ends every session of the signed-in user on the server. */
export function signOut(token: string): Promise<ApiResult<void>> {
  return request<void>("POST", "/api/v1/auth/sign-out", undefined, token);
}

export function getMyParcelsWithoutPrice(token: string): Promise<ApiResult<ParcelSummary[]>> {
  return get<ParcelSummary[]>("/api/v1/me/parcels-without-price", token);
}

// ---------------------------------------------------------------- owner and reviewer workflows

export type ParcelViewer = { isOwnerOfRecord: boolean; myOfferId: string | null; confirmedOfferIds: string[] };

export function getViewer(parcelId: string, token: string): Promise<ApiResult<ParcelViewer>> {
  return get<ParcelViewer>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/viewer`, token);
}

export function setListerConfirmation(parcelId: string, offerId: string, confirm: boolean, token: string): Promise<ApiResult<void>> {
  return request<void>(confirm ? "POST" : "DELETE", `/api/v1/parcels/${encodeURIComponent(parcelId)}/offers/${encodeURIComponent(offerId)}/confirm-lister`, undefined, token);
}

export function getChangeRequestQueue(token: string): Promise<ApiResult<ChangeRequest[]>> {
  return get<ChangeRequest[]>("/api/v1/admin/change-requests?status=pending", token);
}

export function decideChangeRequest(requestId: string, decision: "approve" | "reject", note: string, token: string): Promise<ApiResult<ChangeRequest>> {
  return request<ChangeRequest>("POST", `/api/v1/admin/change-requests/${encodeURIComponent(requestId)}/decision`, { decision, note: note || null }, token);
}

export type DuplicateCandidate = {
  parcelId: string;
  keptParcelId: string;
  village: string;
  mandal: string;
  district: string;
  surveyNumber: string;
  activeOffers: number;
  foundAt: string;
};

export function getDuplicateQueue(token: string): Promise<ApiResult<DuplicateCandidate[]>> {
  return get<DuplicateCandidate[]>("/api/v1/admin/duplicates", token);
}

export function mergeDuplicate(parcelId: string, token: string): Promise<ApiResult<unknown>> {
  return request<unknown>("POST", `/api/v1/admin/duplicates/${encodeURIComponent(parcelId)}/merge`, undefined, token);
}

export type AdminUser = { id: string; phoneMasked: string; displayName: string | null; checks: string[]; createdAt: string };

export function findUserByPhone(phone: string, token: string): Promise<ApiResult<AdminUser>> {
  return get<AdminUser>(`/api/v1/admin/users/by-phone?phone=${encodeURIComponent(phone)}`, token);
}

export function setOwnerOfRecord(parcelId: string, userId: string, evidenceNote: string, token: string): Promise<ApiResult<void>> {
  return request<void>("POST", `/api/v1/admin/parcels/${encodeURIComponent(parcelId)}/owner-of-record`, { userId, evidenceNote }, token);
}

// ---------------------------------------------------------------- documents, subscriptions, contact

export type DocumentType = { code: string; referenceRequired: boolean; imagesOnly: boolean };
export type DocumentTypes = { allowed: DocumentType[]; neverUpload: string[]; maxBytes: number };

export function getDocumentTypes(): Promise<ApiResult<DocumentTypes>> {
  return get<DocumentTypes>("/api/v1/document-types");
}

export type ParcelDocument = {
  id: string;
  type: string;
  referenceNumber: string | null;
  contentType: string;
  sizeBytes: number;
  status: "pending" | "approved" | "rejected";
  reviewNote: string | null;
  uploadedByMe: boolean;
  createdAt: string;
};

export type ParcelDocuments = {
  /** sign_in: not signed in; subscribe: signed in without a viewer subscription; full: may open checked documents. */
  access: "sign_in" | "subscribe" | "full";
  canUpload: boolean;
  uploadBlockedReason: "sign_in" | "not_involved" | "subscribe" | null;
  available: { type: string; count: number }[];
  documents: ParcelDocument[];
};

export function getParcelDocuments(parcelId: string, token?: string): Promise<ApiResult<ParcelDocuments>> {
  return get<ParcelDocuments>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/documents`, token);
}

export function uploadDocument(parcelId: string, form: FormData, token: string): Promise<ApiResult<ParcelDocument>> {
  return request<ParcelDocument>("POST", `/api/v1/parcels/${encodeURIComponent(parcelId)}/documents`, form, token);
}

export function removeDocument(parcelId: string, documentId: string, token: string): Promise<ApiResult<void>> {
  return request<void>("DELETE", `/api/v1/parcels/${encodeURIComponent(parcelId)}/documents/${encodeURIComponent(documentId)}`, undefined, token);
}

/** The raw file response, streamed to the browser by the /files route. */
export async function fetchDocumentFile(parcelId: string, documentId: string, token: string): Promise<Response | ApiProblem> {
  const headers: Record<string, string> = { authorization: `Bearer ${token}` };
  const ip = await visitorIp();
  if (ip) headers["x-forwarded-for"] = ip;
  try {
    const response = await fetch(`${baseUrl()}/api/v1/parcels/${encodeURIComponent(parcelId)}/documents/${encodeURIComponent(documentId)}/file`, {
      cache: "no-store",
      headers,
      signal: AbortSignal.timeout(30000),
    });
    if (response.ok) return response;
    let code = response.status === 401 ? "auth.required" : "api.error";
    try {
      const json = (await response.json()) as { code?: unknown };
      if (typeof json.code === "string") code = json.code;
    } catch {
      // not JSON
    }
    return { ok: false, status: response.status, code };
  } catch (error) {
    console.error("Document download failed", error);
    return { ok: false, status: 503, code: "api.unreachable" };
  }
}

export function getContact(parcelId: string, offerId: string, token: string): Promise<ApiResult<{ whatsappE164: string }>> {
  return get<{ whatsappE164: string }>(`/api/v1/parcels/${encodeURIComponent(parcelId)}/offers/${encodeURIComponent(offerId)}/contact`, token);
}

export type SubscriptionStatus = {
  listerActive: boolean;
  listerUntil: string | null;
  viewerActive: boolean;
  viewerUntil: string | null;
  viewerPlanIsFree: boolean;
  listerMonthlyPriceRupees: number;
  supportWhatsappE164: string | null;
  whatsappE164: string;
};

export function getSubscription(token: string): Promise<ApiResult<SubscriptionStatus>> {
  return get<SubscriptionStatus>("/api/v1/me/subscription", token);
}

export function setWhatsapp(phone: string, token: string): Promise<ApiResult<void>> {
  return request<void>("PUT", "/api/v1/me/whatsapp", { phone }, token);
}

export type AdminDocument = {
  id: string;
  parcelId: string;
  village: string;
  mandal: string;
  district: string;
  surveyNumber: string;
  type: string;
  referenceNumber: string | null;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
};

export function getDocumentQueue(token: string): Promise<ApiResult<AdminDocument[]>> {
  return get<AdminDocument[]>("/api/v1/admin/documents", token);
}

export function decideDocument(documentId: string, decision: "approve" | "reject", note: string, token: string): Promise<ApiResult<void>> {
  return request<void>("POST", `/api/v1/admin/documents/${encodeURIComponent(documentId)}/decision`, { decision, note }, token);
}

export type ActivateSubscription = { phone: string; audience: "lister" | "viewer"; months: number; amountRupees: number; paymentReference: string };

export function activateSubscription(body: ActivateSubscription, token: string): Promise<ApiResult<{ id: string; startsAt: string; endsAt: string }>> {
  return request<{ id: string; startsAt: string; endsAt: string }>("POST", "/api/v1/admin/subscriptions", body, token);
}

export type ParcelPlace = { id: string; location: { district: string; mandal: string; village: string; surveyNumber: string } };

export function getParcelPlace(parcelId: string): Promise<ApiResult<ParcelPlace>> {
  return get<ParcelPlace>(`/api/v1/parcels/${encodeURIComponent(parcelId)}`);
}
