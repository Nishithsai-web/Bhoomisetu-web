import Link from "next/link";
import { notFound } from "next/navigation";
import { searchParcels } from "@/lib/api";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { errorText } from "@/lib/errors";
import { formatNumber } from "@/lib/money";

type Props = {
  params: Promise<{ locale: string }>;
  searchParams: Promise<{ q?: string | string[]; page?: string | string[] }>;
};

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);

export default async function ParcelsPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);
  const query = (first((await searchParams).q) ?? "").trim().slice(0, 100);
  const pageNumber = Math.max(1, Number.parseInt(first((await searchParams).page) ?? "1", 10) || 1);
  const intl = INTL_LOCALE[locale];

  const result = await searchParcels(query, pageNumber);

  return (
    <section className="page">
      <h1>{t.parcels.title}</h1>
      <p className="lead">{query ? format(t.parcels.resultsFor, { query }) : t.parcels.all}</p>

      {!result.ok ? (
        <p role="alert" className="notice">
          {result.status >= 500 ? t.compare.apiDown : errorText(t, result.code)}
        </p>
      ) : result.data.items.length === 0 ? (
        <p className="notice">{t.parcels.none}</p>
      ) : (
        <>
          <ul className="parcel-list">
            {result.data.items.map((parcel) => (
              <li key={parcel.id} className="card">
                <h2>
                  {parcel.village}, {parcel.mandal}, {parcel.district}
                </h2>
                <p>
                  {format(t.parcels.survey, { number: parcel.surveyNumber })} ·{" "}
                  {parcel.areaSquareYards >= 4840
                    ? format(t.parcels.areaAcres, { value: formatNumber(parcel.areaSquareYards / 4840, intl) })
                    : format(t.parcels.area, { value: formatNumber(parcel.areaSquareYards, intl) })}{" "}
                  · {parcel.activeOffers === 1 ? t.parcels.offersOne : format(t.parcels.offers, { count: parcel.activeOffers })}
                </p>
                <Link href={`/${locale}/parcels/${parcel.id}/compare`}>{t.parcels.compare}</Link>
              </li>
            ))}
          </ul>
          <nav className="pager" aria-label={format(t.parcels.page, { page: pageNumber })}>
            {pageNumber > 1 && (
              <Link href={`/${locale}/parcels?${new URLSearchParams({ q: query, page: String(pageNumber - 1) })}`}>{t.parcels.previous}</Link>
            )}
            <span>{format(t.parcels.page, { page: pageNumber })}</span>
            {pageNumber * result.data.pageSize < result.data.total && (
              <Link href={`/${locale}/parcels?${new URLSearchParams({ q: query, page: String(pageNumber + 1) })}`}>{t.parcels.next}</Link>
            )}
          </nav>
        </>
      )}
    </section>
  );
}
