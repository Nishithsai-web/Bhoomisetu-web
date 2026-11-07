import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { removeDocumentAction } from "../../../actions";
import { ConfirmBox } from "@/components/ConfirmBox";
import { UploadDocumentForm } from "@/components/UploadDocumentForm";
import { getDocumentTypes, getParcelDocuments, getParcelPlace, getSubscription, isUuid, type ParcelDocument } from "@/lib/api";
import { formatFileSize } from "@/lib/contact";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatRupees } from "@/lib/money";
import { getSessionToken } from "@/lib/session";

type Props = {
  params: Promise<{ locale: string; id: string }>;
  searchParams: Promise<{ done?: string; error?: string; remove?: string }>;
};

/**
 * Documents for one piece of land. Everyone sees which checked papers exist; subscribed buyers open them;
 * owners and agents with a subscription add new ones from a fixed list of public paper types.
 */
export default async function DocumentsPage({ params, searchParams }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  const d = t.documents;
  const intl = INTL_LOCALE[locale];
  const query = await searchParams;
  const token = (await getSessionToken()) ?? undefined;

  let result = await getParcelDocuments(id, token);
  if (!result.ok && result.status === 401 && token) {
    // Session ended on the server: show the page as a visitor instead of failing.
    result = await getParcelDocuments(id);
  }
  if (!result.ok && result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
    redirect(`/${locale}/parcels/${result.existingParcelId}/documents`);
  }
  if (!result.ok) {
    if (result.status === 404) notFound();
    return (
      <section className="page narrow">
        <h1>{d.title}</h1>
        <p className="notice error" role="alert">
          {errorText(t, result.code)}
        </p>
      </section>
    );
  }

  const data = result.data;
  const [place, types, subscription] = await Promise.all([
    getParcelPlace(id),
    data.canUpload ? getDocumentTypes() : Promise.resolve(null),
    data.uploadBlockedReason === "subscribe" && token ? getSubscription(token) : Promise.resolve(null),
  ]);
  const labels = d.types as Record<string, string>;
  const never = d.never as Record<string, string>;
  const dateFormat = new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" });
  const mine = data.documents.filter((x) => x.uploadedByMe);
  const others = data.documents.filter((x) => !x.uploadedByMe);
  const back = `/${locale}/parcels/${id}/documents`;

  const row = (doc: ParcelDocument) => (
    <li key={doc.id} className="card">
      <p>
        <strong>{labels[doc.type] ?? doc.type}</strong>
        {doc.referenceNumber && (
          <span className="muted">
            {" "}
            · {d.referencePrefix} {doc.referenceNumber}
          </span>
        )}
      </p>
      <p className="hint">
        <span className={`badge badge-doc-${doc.status}`}>{d.status[doc.status]}</span> · {formatFileSize(doc.sizeBytes, intl)} · {dateFormat.format(new Date(doc.createdAt))}
      </p>
      {doc.status === "rejected" && doc.reviewNote && <p className="notice">{format(d.rejectedReason, { note: doc.reviewNote })}</p>}
      {doc.status === "approved" && doc.referenceNumber && <p className="hint">{format(d.verifyYourself, { number: doc.referenceNumber })}</p>}
      <p className="form-inline">
        {doc.status !== "rejected" && (
          <a className="button-link" href={`/files/${id}/${doc.id}?l=${locale}`}>
            {d.open}
          </a>
        )}
        {doc.uploadedByMe && query.remove !== doc.id && (
          <Link className="button-link" href={`${back}?remove=${doc.id}`}>
            {d.remove}
          </Link>
        )}
      </p>
      {/* Removing cannot be undone, so it asks first. */}
      {doc.uploadedByMe && query.remove === doc.id && (
        <ConfirmBox id={`remove-${doc.id}`} question={d.confirmRemove}>
          <form action={removeDocumentAction} className="form-inline">
            <input type="hidden" name="locale" value={locale} />
            <input type="hidden" name="parcelId" value={id} />
            <input type="hidden" name="documentId" value={doc.id} />
            <button type="submit">{d.yesRemove}</button> <Link href={back}>{d.cancel}</Link>
          </form>
        </ConfirmBox>
      )}
    </li>
  );

  return (
    <section className="page narrow">
      <h1>{d.title}</h1>
      {place.ok && (
        <p className="lead">
          {place.data.location.village}, {place.data.location.mandal}, {place.data.location.district} · {place.data.location.surveyNumber}
        </p>
      )}
      <p>{d.intro}</p>
      <p className="hint">{d.reviewNote}</p>

      {query.done === "uploaded" && (
        <p className="notice" role="status">
          {d.uploaded}
        </p>
      )}
      {query.done === "removed" && (
        <p className="notice" role="status">
          {d.removed}
        </p>
      )}
      {query.error && (
        <p className="notice error" role="alert">
          {errorText(t, query.error)}
        </p>
      )}

      <div className="safety">
        <h2>{d.safetyTitle}</h2>
        <p>{d.safety}</p>
      </div>

      <h2>{d.available}</h2>
      {data.available.length === 0 ? (
        <p>{d.noneAvailable}</p>
      ) : (
        <ul>
          {data.available.map((a) => (
            <li key={a.type}>
              {labels[a.type] ?? a.type}: {a.count}
            </li>
          ))}
        </ul>
      )}

      {data.access === "sign_in" && data.available.length > 0 && (
        <p className="notice">
          <Link href={`/${locale}/signin?next=${encodeURIComponent(back)}`}>{d.signInToView}</Link>
        </p>
      )}
      {data.access === "subscribe" && data.available.length > 0 && (
        <p className="notice">
          {d.subscribeToView} {d.buyerHowTo}
        </p>
      )}

      {others.length > 0 && (
        <>
          <h2>{d.list}</h2>
          <ul className="parcel-list">{others.map(row)}</ul>
        </>
      )}

      {mine.length > 0 && (
        <>
          <h2>{d.yours}</h2>
          <ul className="parcel-list">{mine.map(row)}</ul>
        </>
      )}

      {data.canUpload && types?.ok && (
        <>
          <h2>{d.uploadTitle}</h2>
          <div className="notice">
            <p>
              <strong>{d.neverTitle}</strong>
            </p>
            <ul>
              {types.data.neverUpload.map((code) => (
                <li key={code}>{never[code] ?? code}</li>
              ))}
            </ul>
          </div>
          <UploadDocumentForm locale={locale} parcelId={id} types={types.data.allowed} maxBytes={types.data.maxBytes} t={t} />
        </>
      )}
      {data.uploadBlockedReason === "subscribe" && (
        <p className="notice">
          {format(d.listerNeedsSubscription, {
            price: subscription?.ok ? formatRupees(subscription.data.listerMonthlyPriceRupees, intl) : "₹",
          })}{" "}
          <Link href={`/${locale}/mine`}>{t.subscription.title}</Link>
        </p>
      )}

      <p>
        <Link href={`/${locale}/parcels/${id}/compare`}>{t.compare.title}</Link>
      </p>
    </section>
  );
}
