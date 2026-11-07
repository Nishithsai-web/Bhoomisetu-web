import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { myOfferAction, setWhatsappAction, signOutAction } from "../actions";
import { ConfirmBox } from "@/components/ConfirmBox";
import { getMyChangeRequests, getMyOffers, getMyParcelsWithoutPrice, getSubscription, type MyOfferAction } from "@/lib/api";
import { formatIndianMobile, whatsappLink } from "@/lib/contact";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatRupees, formatRupeesShort } from "@/lib/money";
import { clearSession, getSessionToken } from "@/lib/session";

type Props = { params: Promise<{ locale: string }>; searchParams: Promise<{ done?: string; error?: string; confirm?: string; parcel?: string }> };

export default async function MyListingsPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/mine`);

  const t = getDictionary(locale);
  const m = t.mine;
  const intl = INTL_LOCALE[locale];
  const dateFormat = new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" });
  const query = await searchParams;
  const [offers, requests, unpriced, subscription] = await Promise.all([getMyOffers(token), getMyChangeRequests(token), getMyParcelsWithoutPrice(token), getSubscription(token)]);
  if (!offers.ok && offers.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/mine`);
  }

  const done = query.done && Object.hasOwn(m.done, query.done) ? m.done[query.done as keyof typeof m.done] : null;
  // "Mark as sold" and "Remove" first ask "are you sure?" on the same page, so one stray tap changes nothing.
  const confirmAction: MyOfferAction | null = query.confirm === "sold" || query.confirm === "withdraw" ? query.confirm : null;
  const confirming = confirmAction && query.parcel ? { action: confirmAction, parcelId: query.parcel } : null;

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
                  {confirming?.parcelId === offer.parcelId ? (
                    <ConfirmBox id={`confirm-${offer.offerId}`} question={confirming.action === "sold" ? m.confirmSold : m.confirmWithdraw}>
                      <div className="actions">
                        {button(offer.parcelId, confirming.action, m.yes)}
                        <Link className="button-link" href={`/${locale}/mine`}>
                          {m.cancel}
                        </Link>
                      </div>
                    </ConfirmBox>
                  ) : (
                    <div className="actions">
                      {button(offer.parcelId, "still-available", m.stillAvailable)}
                      <Link className="button-link" href={`/${locale}/mine?confirm=sold&parcel=${offer.parcelId}`}>
                        {m.sold}
                      </Link>
                      <Link className="button-link" href={`/${locale}/mine?confirm=withdraw&parcel=${offer.parcelId}`}>
                        {m.withdraw}
                      </Link>
                    </div>
                  )}
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

      {unpriced.ok && unpriced.data.length > 0 && (
        <>
          <h2>{m.unpriced}</h2>
          <ul className="parcel-list">
            {unpriced.data.map((p) => (
              <li key={p.id} className="card">
                <p>
                  {p.village}, {p.mandal} · {format(t.parcels.survey, { number: p.surveyNumber })}
                </p>
                <Link className="button-link" href={`/${locale}/parcels/${p.id}/offer`}>
                  {m.addPrice}
                </Link>
              </li>
            ))}
          </ul>
        </>
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
      {subscription.ok && (
        <>
          <h2>{t.subscription.title}</h2>
          <div className="card">
            <p>
              {subscription.data.listerActive && subscription.data.listerUntil
                ? format(t.subscription.listerActive, { date: dateFormat.format(new Date(subscription.data.listerUntil)) })
                : t.subscription.listerInactive}
            </p>
            {subscription.data.listerActive && (
              <p className="hint">{format(t.subscription.contactNumber, { number: formatIndianMobile(subscription.data.whatsappE164) })}</p>
            )}
            {!subscription.data.listerActive && (
              <>
                <p>{format(t.subscription.price, { price: formatRupees(subscription.data.listerMonthlyPriceRupees, intl) })}</p>
                <p className="hint">{t.subscription.howToPay}</p>
                {subscription.data.supportWhatsappE164 && whatsappLink(subscription.data.supportWhatsappE164, t.subscription.supportMessage) && (
                  <p>
                    <a className="button-link" href={whatsappLink(subscription.data.supportWhatsappE164, t.subscription.supportMessage) as string}>
                      {t.subscription.support}
                    </a>
                  </p>
                )}
              </>
            )}
            <p>
              {subscription.data.viewerPlanIsFree
                ? t.subscription.viewerFree
                : subscription.data.viewerActive && subscription.data.viewerUntil
                  ? format(t.subscription.viewerActive, { date: dateFormat.format(new Date(subscription.data.viewerUntil)) })
                  : t.subscription.viewerInactive}
            </p>
          </div>

          <h2>{t.subscription.whatsappTitle}</h2>
          <form action={setWhatsappAction} className="card form">
            <input type="hidden" name="locale" value={locale} />
            <div className="field">
              <label htmlFor="whatsapp">{t.subscription.whatsappLabel}</label>
              <p id="whatsapp-hint" className="hint">
                {format(t.subscription.whatsappHint, { number: formatIndianMobile(subscription.data.whatsappE164) })}
              </p>
              <input id="whatsapp" name="whatsapp" type="tel" inputMode="tel" autoComplete="tel-national" aria-describedby="whatsapp-hint" />
            </div>
            <button type="submit">{t.subscription.save}</button>
          </form>
        </>
      )}

      <form action={signOutAction} className="form-inline">
        <input type="hidden" name="locale" value={locale} />
        <button type="submit" className="secondary">
          {t.nav.signOut}
        </button>
      </form>
    </section>
  );
}
