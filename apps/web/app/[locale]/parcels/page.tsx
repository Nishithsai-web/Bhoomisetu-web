import Link from "next/link";
import { notFound } from "next/navigation";
import { LAND_CLASSES, isLandClass, searchParcels } from "@/lib/api";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupeesShort } from "@/lib/money";

type Props = {
  params: Promise<{ locale: string }>;
  searchParams: Promise<{ q?: string | string[]; page?: string | string[]; landClass?: string | string[] }>;
};

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);

/** Search results: a search box, land-type pills, and one card per land with its lowest price and checks. */
export default async function ParcelsPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);
  const p = t.parcels;
  const raw = await searchParams;
  const query = (first(raw.q) ?? "").trim().slice(0, 100);
  const requestedClass = first(raw.landClass) ?? "";
  const landClass = isLandClass(requestedClass) ? requestedClass : "";
  const pageNumber = Math.max(1, Number.parseInt(first(raw.page) ?? "1", 10) || 1);
  const intl = INTL_LOCALE[locale];
  const classNames = t.sell.landClasses as Record<string, string>;

  const result = await searchParcels(query, pageNumber, landClass || undefined);
  const link = (changes: Record<string, string>) => {
    const next = new URLSearchParams({ ...(query ? { q: query } : {}), ...(landClass ? { landClass } : {}), ...changes });
    for (const [key, value] of [...next.entries()]) if (value === "") next.delete(key);
    const text = next.toString();
    return `/${locale}/parcels${text ? `?${text}` : ""}`;
  };

  return (
    <section className="page">
      <h1>{p.title}</h1>
      <form action={`/${locale}/parcels`} method="get" role="search" className="search">
        <label htmlFor="q">{t.home.searchLabel}</label>
        <div className="search-row">
          <input id="q" name="q" type="search" defaultValue={query} placeholder={t.home.searchPlaceholder} maxLength={100} autoComplete="off" />
          {landClass && <input type="hidden" name="landClass" value={landClass} />}
          <button type="submit">{t.home.searchButton}</button>
        </div>
      </form>

      <nav aria-label={p.filterLabel} className="pills">
        <span>{p.filterLabel}</span>
        <Link href={link({ landClass: "", page: "" })} aria-current={landClass === "" ? "page" : undefined} className={landClass === "" ? "active" : undefined}>
          {p.filterAll}
        </Link>
        {LAND_CLASSES.filter((c) => c !== "unknown").map((c) => (
          <Link key={c} href={link({ landClass: c, page: "" })} aria-current={landClass === c ? "page" : undefined} className={landClass === c ? "active" : undefined}>
            {t.home.types[c].title}
          </Link>
        ))}
      </nav>

      {!result.ok ? (
        <p role="alert" className="notice">
          {result.status >= 500 ? t.compare.apiDown : errorText(t, result.code)}
        </p>
      ) : result.data.items.length === 0 ? (
        <p className="notice">{p.none}</p>
      ) : (
        <>
          <p className="lead">
            {query ? format(p.resultsFor, { query }) : p.all} · {format(p.resultsCount, { count: formatNumber(result.data.total, intl, 0) })}
          </p>
          {query && result.data.items[0] && (
            <p>
              {/* The map searches places, so it gets the village of the best match, not the whole search text. */}
              <Link href={`/${locale}/map?q=${encodeURIComponent(result.data.items[0].village)}`} className="button-link">
                {t.landMap.searchOnMap}
              </Link>
            </p>
          )}
          <ul className="parcel-list">
            {result.data.items.map((parcel) => (
              <li key={parcel.id} className="result">
                <ul className="tags">
                  <li className="tag tag-class">{classNames[parcel.landClass] ?? parcel.landClass}</li>
                  {parcel.ownerOfRecordRecorded && <li className="tag">{p.ownerRecorded}</li>}
                  {parcel.reviewedDocuments > 0 && <li className="tag">{format(p.reviewedDocs, { count: parcel.reviewedDocuments })}</li>}
                </ul>
                <h2>
                  <Link href={`/${locale}/parcels/${parcel.id}`}>
                    {parcel.village}, {parcel.mandal}, {parcel.district}
                  </Link>
                </h2>
                <p className="price">
                  {parcel.lowestPriceRupees === null
                    ? p.noPrice
                    : format(p.fromPrice, { price: formatRupeesShort(parcel.lowestPriceRupees, intl, t.amounts) })}{" "}
                  <span className="muted">· {parcel.activeOffers === 1 ? p.offersOne : format(p.offers, { count: parcel.activeOffers })}</span>
                </p>
                <p className="muted">
                  {format(p.survey, { number: parcel.surveyNumber })} ·{" "}
                  {parcel.areaSquareYards >= 4840
                    ? format(p.areaAcres, { value: formatNumber(parcel.areaSquareYards / 4840, intl) })
                    : format(p.area, { value: formatNumber(parcel.areaSquareYards, intl) })}
                </p>
                <p className="form-inline">
                  <Link href={`/${locale}/parcels/${parcel.id}`} className="button-link"
                    aria-label={`${p.viewLand}: ${parcel.village}, ${format(p.survey, { number: parcel.surveyNumber })}`}>
                    {p.viewLand}
                  </Link>
                  {parcel.activeOffers > 0 && <Link href={`/${locale}/parcels/${parcel.id}/compare`}>{p.compare}</Link>}
                </p>
              </li>
            ))}
          </ul>
          <nav className="pager" aria-label={format(p.page, { page: pageNumber })}>
            {pageNumber > 1 && <Link href={link({ page: String(pageNumber - 1) })}>{p.previous}</Link>}
            <span>{format(p.page, { page: pageNumber })}</span>
            {pageNumber * result.data.pageSize < result.data.total && <Link href={link({ page: String(pageNumber + 1) })}>{p.next}</Link>}
          </nav>
        </>
      )}
    </section>
  );
}
