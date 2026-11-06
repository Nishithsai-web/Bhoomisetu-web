import Link from "next/link";
import { notFound } from "next/navigation";
import { SORTS, getComparison, isSort, isUuid, type Sort } from "@/lib/api";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupees, formatRupeesShort } from "@/lib/money";

type Props = {
  params: Promise<{ locale: string; id: string }>;
  searchParams: Promise<{ sort?: string | string[] }>;
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
  const dateFormat = new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" });
  const words = { lakh: t.amounts.lakh, crore: t.amounts.crore };

  return (
    <section className="page">
      <h1>{c.title}</h1>
      <p className="lead">
        {format(c.summary, {
          count: data.offers.length,
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
                <th scope="col">{c.columns.perGajam}</th>
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
                    {offer.listerLabel}
                    <span className="muted"> · {c.listerKind[offer.listerKind]}</span>
                    <ul className="badges">
                      {offer.badges.map((badge) => (
                        <li key={badge} className={`badge badge-${badge}`}>
                          {c.badges[badge]}
                        </li>
                      ))}
                    </ul>
                  </th>
                  <td>{formatRupees(offer.effectivePriceRupees, intl)}</td>
                  <td>{formatRupees(offer.effectivePricePerSquareYardRupees, intl)}</td>
                  <td>
                    {offer.listerKind === "owner" || offer.buyerBrokeragePercent === 0
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
