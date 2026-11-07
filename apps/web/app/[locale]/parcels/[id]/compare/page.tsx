import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ownerConfirmAction } from "../../../actions";
import { SORTS, getComparison, getViewer, isSort, isUuid, type ParcelViewer, type Sort } from "@/lib/api";
import { errorText } from "@/lib/errors";
import { getSessionToken } from "@/lib/session";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupees, formatRupeesShort } from "@/lib/money";

type Props = {
  params: Promise<{ locale: string; id: string }>;
  searchParams: Promise<{ sort?: string | string[]; done?: string; error?: string }>;
};

const percent = (weight: number) => Math.round(weight * 100);

export default async function ComparePage({ params, searchParams }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  const c = t.compare;
  const intl = INTL_LOCALE[locale];

  const requested = (await searchParams).sort;
  const rawSort = Array.isArray(requested) ? requested[0] : requested;
  const sort: Sort = isSort(rawSort) ? rawSort : "best_value";
  const invalidSort = rawSort !== undefined && !isSort(rawSort);

  const result = await getComparison(id, sort);
  if (!result.ok && result.code === "parcel.duplicate" && result.existingParcelId && isUuid(result.existingParcelId)) {
    // This record was merged into another one for the same land.
    redirect(`/${locale}/parcels/${result.existingParcelId}/compare?sort=${sort}`);
  }
  if (!result.ok) {
    return (
      <section className="page">
        <h1>{c.title}</h1>
        <p role="alert" className="notice">
          {result.status === 404 ? c.notFound : c.apiDown}
        </p>
      </section>
    );
  }

  const data = result.data;
  const query = await searchParams;

  // Signed-in owners of record get confirm / remove buttons on each agent's row.
  const token = await getSessionToken();
  const viewerResult = token ? await getViewer(id, token) : null;
  const viewer: ParcelViewer | null = viewerResult?.ok ? viewerResult.data : null;
  const dateFormat = new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" });
  const words = t.amounts;
  // Farmland is priced per acre, house plots per gajam: show whichever buyers of this land use.
  const perAcre = data.landClass === "agricultural";

  return (
    <section className="page">
      <h1>{c.title}</h1>
      <p className="lead">
        {format(c.summary, {
          offers: data.offers.length === 1 ? t.parcels.offersOne : format(t.parcels.offers, { count: data.offers.length }),
          area: formatNumber(data.areaSquareYards, intl),
          acres: formatNumber(data.areaAcres, intl, 4),
          median: data.medianEffectivePriceRupees === null ? c.unknown : formatRupeesShort(data.medianEffectivePriceRupees, intl, words),
        })}
      </p>

      {invalidSort && (
        <p role="status" className="notice">
          {c.invalidSort}
        </p>
      )}

      {query.done === "owner" && (
        <p className="notice" role="status">
          {c.ownerSaved}
        </p>
      )}
      {query.error && (
        <p className="notice error" role="alert">
          {errorText(t, query.error)}
        </p>
      )}
      {viewer?.isOwnerOfRecord && <p className="notice">{c.ownerHelp}</p>}

      <nav aria-label={c.sortLabel} className="sort-tabs">
        <span>{c.sortLabel}</span>
        {SORTS.map((s) => (
          <Link key={s} href={`/${locale}/parcels/${id}/compare?sort=${s}`} aria-current={s === sort ? "page" : undefined} className={s === sort ? "active" : undefined}>
            {c.sorts[s]}
          </Link>
        ))}
      </nav>

      {data.offers.length === 0 ? (
        <p className="notice">{c.noOffers}</p>
      ) : (
        <div className="table-wrap" tabIndex={0} role="region" aria-label={c.title}>
          <table className="compare-table">
            <thead>
              <tr>
                <th scope="col">{c.columns.rank}</th>
                <th scope="col">{c.columns.lister}</th>
                <th scope="col">{c.columns.price}</th>
                <th scope="col">{perAcre ? c.columns.perAcre : c.columns.perGajam}</th>
                <th scope="col">{c.columns.brokerage}</th>
                <th scope="col">{c.columns.authority}</th>
                <th scope="col">{c.columns.checks}</th>
                <th scope="col">{c.columns.response}</th>
                <th scope="col">{c.columns.confirmed}</th>
                <th scope="col">{c.columns.score}</th>
              </tr>
            </thead>
            <tbody>
              {data.offers.map((offer) => (
                <tr key={offer.offerId} className={offer.badges.includes("best_value") ? "highlight" : undefined}>
                  <td>{offer.rank}</td>
                  <th scope="row">
                    {offer.listerName ?? format(c.listerNumber, { number: offer.listerNumber })}
                    <span className="muted"> · {offer.ownerClaimUnverified ? c.ownerUnverified : c.listerKind[offer.listerKind]}</span>
                    <ul className="badges">
                      {offer.badges.map((badge) => (
                        <li key={badge} className={`badge badge-${badge}`}>
                          {c.badges[badge]}
                        </li>
                      ))}
                    </ul>
                    {/* An "owner (not verified)" claim is not an agent, so there is nothing to confirm; removing an old confirmation stays possible. */}
                    {viewer?.isOwnerOfRecord && offer.offerId !== viewer.myOfferId && (!offer.ownerClaimUnverified || viewer.confirmedOfferIds.includes(offer.offerId)) && (
                      <form action={ownerConfirmAction} className="form-inline">
                        <input type="hidden" name="locale" value={locale} />
                        <input type="hidden" name="parcelId" value={id} />
                        <input type="hidden" name="offerId" value={offer.offerId} />
                        {viewer.confirmedOfferIds.includes(offer.offerId) ? (
                          <button type="submit" name="intent" value="remove" className="secondary"
                            aria-label={`${c.removeConfirmation}: ${offer.listerName ?? format(c.listerNumber, { number: offer.listerNumber })}`}>
                            {c.removeConfirmation}
                          </button>
                        ) : (
                          <button type="submit" name="intent" value="confirm"
                            aria-label={`${c.confirmAgent}: ${offer.listerName ?? format(c.listerNumber, { number: offer.listerNumber })}`}>
                            {c.confirmAgent}
                          </button>
                        )}
                      </form>
                    )}
                  </th>
                  <td>{formatRupees(offer.effectivePriceRupees, intl)}</td>
                  <td>{formatRupees(perAcre ? offer.effectivePricePerAcreRupees : offer.effectivePricePerSquareYardRupees, intl)}</td>
                  <td>
                    {(offer.listerKind === "owner" && !offer.ownerClaimUnverified) || offer.buyerBrokeragePercent === 0
                      ? c.brokerageNone
                      : offer.brokerageAssumed
                        ? format(c.brokerageAssumed, { percent: formatNumber(data.rules.assumedUndisclosedBrokeragePercent, intl) })
                        : `${formatNumber(offer.buyerBrokeragePercent ?? 0, intl)}%`}
                  </td>
                  <td>{c.authority[offer.authority]}</td>
                  <td>{offer.checks.length === 0 ? c.noChecks : offer.checks.map((check) => c.checks[check]).join(", ")}</td>
                  <td>
                    {offer.medianResponseMinutes === null
                      ? c.unknown
                      : format(c.hours, { hours: formatNumber(offer.medianResponseMinutes / 60, intl, 1) })}
                  </td>
                  <td>{dateFormat.format(new Date(offer.availabilityConfirmedAt))}</td>
                  <td>{formatNumber(offer.score * 100, intl, 0)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <aside className="card explain">
        <h2>{c.howTitle}</h2>
        <p>
          {format(c.howBody, {
            price: percent(data.rules.priceWeight),
            authority: percent(data.rules.authorityWeight),
            checks: percent(data.rules.checksWeight),
            response: percent(data.rules.responseWeight),
            freshness: percent(data.rules.freshnessWeight),
            outlier: percent(data.rules.lowPriceOutlierRatio),
          })}
        </p>
      </aside>
      <p className="safety" role="note">
        {t.home.safety}
      </p>
    </section>
  );
}
