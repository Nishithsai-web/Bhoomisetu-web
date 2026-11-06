import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { myOfferAction } from "../actions";
import { getMyChangeRequests, getMyOffers, type MyOfferAction } from "@/lib/api";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatRupeesShort } from "@/lib/money";
import { clearSession, getSessionToken } from "@/lib/session";

type Props = { params: Promise<{ locale: string }>; searchParams: Promise<{ done?: string; error?: string }> };

export default async function MyListingsPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/mine`);

  const t = getDictionary(locale);
  const m = t.mine;
  const intl = INTL_LOCALE[locale];
  const query = await searchParams;
  const [offers, requests] = await Promise.all([getMyOffers(token), getMyChangeRequests(token)]);
  if (!offers.ok && offers.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/mine`);
  }

  const done = query.done && query.done in m.done ? m.done[query.done as keyof typeof m.done] : null;

  const button = (parcelId: string, action: MyOfferAction, label: string, style?: string) => (
    <form action={myOfferAction}>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      <input type="hidden" name="action" value={action} />
      <button type="submit" className={style}>
        {label}
      </button>
    </form>
  );

  return (
    <section className="page">
      <h1>{m.title}</h1>
      <p className="lead">{m.intro}</p>
      {done && (
        <p className="notice" role="status">
          {done}
        </p>
      )}
      {query.error && (
        <p className="notice error" role="alert">
          {errorText(t, query.error)}
        </p>
      )}

      {!offers.ok ? (
        <p className="notice" role="alert">
          {errorText(t, offers.code)}
        </p>
      ) : offers.data.length === 0 ? (
        <p className="notice">
          {m.empty} <Link href={`/${locale}/sell`}>{m.listLand}</Link>
        </p>
      ) : (
        <ul className="parcel-list">
          {offers.data.map((offer) => (
            <li key={offer.offerId} className={offer.needsConfirmation ? "card attention" : "card"}>
              <h2>
                {offer.village}, {offer.mandal} · {format(t.parcels.survey, { number: offer.surveyNumber })}
              </h2>
              <p>
                <strong>{formatRupeesShort(offer.askingPriceRupees, intl, t.amounts)}</strong> · {m.status[offer.status]}
              </p>
              {offer.status === "active" && (
                <>
                  <p className="hint">
                    {offer.daysSinceConfirmed === 0 ? m.confirmedToday : format(m.confirmedDaysAgo, { days: offer.daysSinceConfirmed })}
                  </p>
                  {offer.needsConfirmation && <p className="notice">{m.needsConfirmation}</p>}
                  <div className="actions">
                    {button(offer.parcelId, "still-available", m.stillAvailable)}
                    {button(offer.parcelId, "sold", m.sold, "secondary")}
                    {button(offer.parcelId, "withdraw", m.withdraw, "secondary")}
                  </div>
                </>
              )}
              <p>
                <Link href={`/${locale}/parcels/${offer.parcelId}/compare`}>{t.parcels.compare}</Link> ·{" "}
                <Link href={`/${locale}/parcels/${offer.parcelId}/correct`}>{m.correct}</Link>
              </p>
            </li>
          ))}
        </ul>
      )}

      {requests.ok && requests.data.length > 0 && (
        <>
          <h2>{m.requests}</h2>
          <ul className="parcel-list">
            {requests.data.map((r) => (
              <li key={r.id} className="card">
                <p>
                  <strong>{t.correct.fields[r.field as keyof typeof t.correct.fields] ?? r.field}</strong>: {r.currentValue ?? "—"} → {r.proposedValue}
                </p>
                <p className="hint">
                  {m.requestStatus[r.status]}
                  {r.decisionNote ? ` · ${r.decisionNote}` : ""}
                </p>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
