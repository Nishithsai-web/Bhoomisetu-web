import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getComparison, getParcel, getParcelDocuments, isUuid } from "@/lib/api";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupeesShort } from "@/lib/money";

type Props = { params: Promise<{ locale: string; id: string }> };

/** Units shown as tiles, in the order village buyers read them: farm units first, then plot units. */
const UNITS = ["acre", "gunta", "square_yard", "cent", "square_foot", "square_metre"] as const;

/**
 * One piece of land: where it is, its area in every unit, size and road access, what was checked, and the way
 * to every price and document. Facts come from the lister and say so; nothing is claimed beyond what was checked.
 */
export default async function LandPage({ params }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  const l = t.land;
  const s = t.sell;
  const intl = INTL_LOCALE[locale];

  const [parcel, comparison, documents] = await Promise.all([getParcel(id), getComparison(id, "best_value"), getParcelDocuments(id)]);
  if (!comparison.ok && comparison.code === "parcel.duplicate" && comparison.existingParcelId && isUuid(comparison.existingParcelId)) {
    redirect(`/${locale}/parcels/${comparison.existingParcelId}`);
  }
  if (!parcel.ok) {
    if (parcel.status === 404) notFound();
    return (
      <section className="page">
        <p role="alert" className="notice">
          {t.compare.apiDown}
        </p>
      </section>
    );
  }

  const p = parcel.data;
  const names = t.units.names as Record<string, string>;
  const label = (map: Record<string, string>, key: string | null) => (key ? (map[key] ?? key) : l.notGiven);
  const offers = comparison.ok ? comparison.data.offers : [];
  const lowest = offers.length > 0 ? Math.min(...offers.map((o) => o.askingPriceRupees)) : null;
  const ownerRecorded = p.ownerOfRecordRecorded;
  const reviewed = documents.ok ? documents.data.available.reduce((sum, a) => sum + a.count, 0) : 0;
  const n = (value: number, digits = 2) => formatNumber(value, intl, digits);

  return (
    <section className="page">
      <p>
        <Link href={`/${locale}/parcels`}>{l.backToResults}</Link>
      </p>
      <ul className="tags">
        <li className="tag tag-class">{label(s.landClasses as Record<string, string>, p.landClass)}</li>
      </ul>
      <h1>
        {p.location.village}, {p.location.mandal}, {p.location.district}
      </h1>
      <p className="lead">
        {format(l.survey, { number: p.location.surveyNumber })} ·{" "}
        {lowest === null ? l.noOffers : format(l.priceFrom, { price: formatRupeesShort(lowest, intl, t.amounts) })}
      </p>

      <div className="detail">
        <div>
          <section className="card" aria-labelledby="area">
            <h2 id="area">{l.areaTitle}</h2>
            <dl className="unit-tiles">
              {UNITS.map((unit) => (
                <div key={unit}>
                  <dt>{names[unit] ?? unit}</dt>
                  <dd>{n(p.area.conversions.units[unit] ?? 0, unit === "acre" ? 4 : 2)}</dd>
                </div>
              ))}
            </dl>
            <p className="hint">
              {format(l.areaNote, {
                value: n(p.area.enteredValue, 4),
                unit: names[p.area.enteredUnit] ?? p.area.enteredUnit,
                source: label(s.areaSources as Record<string, string>, p.area.source),
              })}
            </p>
          </section>

          <section className="card" aria-labelledby="size">
            <h2 id="size">{l.sizeTitle}</h2>
            <table className="facts">
              <tbody>
                <tr>
                  <th scope="row">{l.dimensions}</th>
                  <td>
                    {p.dimensions.frontageFeet !== null && p.dimensions.depthFeet !== null
                      ? format(l.dimensionsValue, { width: n(p.dimensions.frontageFeet, 1), length: n(p.dimensions.depthFeet, 1) })
                      : l.notGiven}
                  </td>
                </tr>
                <tr>
                  <th scope="row">{l.facing}</th>
                  <td>{label(s.facings as Record<string, string>, p.dimensions.facing)}</td>
                </tr>
                {p.dimensions.dimensionsMatchArea !== null && (
                  <tr>
                    <th scope="row">{l.areaCheck}</th>
                    <td>{p.dimensions.dimensionsMatchArea ? l.areaMatch : <strong>{l.areaMismatch}</strong>}</td>
                  </tr>
                )}
                <tr>
                  <th scope="row">{l.roadWidth}</th>
                  <td>
                    {p.road.widthFeet !== null && p.road.widthM !== null
                      ? format(l.roadWidthValue, { feet: n(p.road.widthFeet, 1), metres: n(p.road.widthM, 1) })
                      : l.notGiven}
                    {p.road.widthSource && <span className="muted"> · {label(s.roadWidthSources as Record<string, string>, p.road.widthSource)}</span>}
                  </td>
                </tr>
                <tr>
                  <th scope="row">{l.roadType}</th>
                  <td>{label(s.roadTypes as Record<string, string>, p.road.type)}</td>
                </tr>
                <tr>
                  <th scope="row">{l.accessRight}</th>
                  <td>{label(s.accessRights as Record<string, string>, p.road.accessRight)}</td>
                </tr>
                <tr>
                  <th scope="row">{l.mainRoad}</th>
                  <td>{p.road.distanceToMainRoadM !== null ? format(l.mainRoadValue, { metres: n(p.road.distanceToMainRoadM, 0) }) : l.notGiven}</td>
                </tr>
              </tbody>
            </table>
            <p className="hint">{l.sizeNote}</p>
          </section>

          <section className="card" aria-labelledby="facts">
            <h2 id="facts">{l.factsTitle}</h2>
            <table className="facts">
              <tbody>
                <tr>
                  <th scope="row">{s.surveyNumber}</th>
                  <td>{p.location.surveyNumber}</td>
                </tr>
                <tr>
                  <th scope="row">{l.landClass}</th>
                  <td>{label(s.landClasses as Record<string, string>, p.landClass)}</td>
                </tr>
                <tr>
                  <th scope="row">{l.location}</th>
                  <td>
                    {p.location.village}, {p.location.mandal}, {p.location.district} ({p.location.stateCode})
                  </td>
                </tr>
              </tbody>
            </table>
          </section>
        </div>

        <aside className="stack">
          <section className="panel-dark">
            <h2>{l.checkedTitle}</h2>
            <ul>
              <li>{ownerRecorded ? l.ownerYes : l.ownerNo}</li>
              <li>{reviewed > 0 ? format(l.docsLine, { count: reviewed }) : l.docsNone}</li>
              <li>{l.titleNot}</li>
            </ul>
            <p className="small">{l.titleNote}</p>
          </section>
          <section className="card stack">
            <h2>{l.nextTitle}</h2>
            {offers.length > 0 ? (
              <Link href={`/${locale}/parcels/${id}/compare`} className="button-link primary">
                {offers.length === 1 ? l.pricesLinkOne : format(l.pricesLink, { count: offers.length })}
              </Link>
            ) : (
              <p className="muted">{l.noOffers}</p>
            )}
            <Link href={`/${locale}/parcels/${id}/documents`} className="button-link">
              {l.documents}
            </Link>
            <Link href={`/${locale}/parcels/${id}/offer`} className="button-link">
              {l.addPrice}
            </Link>
            <Link href={`/${locale}/parcels/${id}/correct`}>{l.correct}</Link>
            <p className="safety">{t.home.safety}</p>
          </section>
        </aside>
      </div>
    </section>
  );
}
