import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { getComparison, getLandComparison, getNearbyDevelopments, getParcel, getParcelDocuments, getRentalsNearLand, getViewer, isUuid } from "@/lib/api";
import { PointsList } from "@/components/PointsList";
import { getSessionToken } from "@/lib/session";
import { SetPinForm } from "@/components/SetPinForm";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupees, formatRupeesShort } from "@/lib/money";

type Props = { params: Promise<{ locale: string; id: string }>; searchParams: Promise<{ done?: string; error?: string; nearby?: string }> };

/** Units shown as tiles, in the order village buyers read them: farm units first, then plot units. */
const UNITS = ["acre", "gunta", "square_yard", "cent", "square_foot", "square_metre"] as const;

/**
 * One piece of land: where it is, its area in every unit, size and road access, what was checked, and the way
 * to every price and document. Facts come from the lister and say so; nothing is claimed beyond what was checked.
 */
export default async function LandPage({ params, searchParams }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  const l = t.land;
  const s = t.sell;
  const intl = INTL_LOCALE[locale];

  const [parcel, comparison, documents, nearby] = await Promise.all([
    getParcel(id),
    getComparison(id, "best_value"),
    getParcelDocuments(id),
    getNearbyDevelopments(id),
  ]);
  const { done, error, nearby: nearbyParam } = await searchParams;
  const nearbyMode = nearbyParam === "rent" ? "rent" : "land";
  const hasPin = parcel.ok && parcel.data.pin !== null;
  const [landComparison, nearbyRentals] = await Promise.all([
    hasPin && nearbyMode === "land" ? getLandComparison(id) : Promise.resolve(null),
    hasPin && nearbyMode === "rent" ? getRentalsNearLand(id) : Promise.resolve(null),
  ]);
  const token = await getSessionToken();
  const viewer = token ? await getViewer(id, token) : null;
  // Corrections are open to people with a stake in the land (owner of record or a lister with a price).
  const involved = Boolean(viewer?.ok && (viewer.data.isOwnerOfRecord || viewer.data.myOfferId));
  // The pin: whoever listed the land, its owner of record, or a reviewer (agents with a price cannot move it).
  const mayPin = Boolean(viewer?.ok && (viewer.data.listedByMe || viewer.data.isOwnerOfRecord || viewer.data.isReviewer));
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
  // Same rule as the comparison: brokerage included, offers flagged "verify before visiting" left out.
  const fair = offers.filter((o) => !o.badges.includes("verify_before_visiting"));
  const lowest = offers.length > 0 ? Math.min(...(fair.length > 0 ? fair : offers).map((o) => o.effectivePriceRupees)) : null;
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

      {done === "pin" && (
        <p className="notice" role="status">
          {t.map.saved}
        </p>
      )}
      {error && (
        <p className="notice error" role="alert">
          {errorText(t, error)}
        </p>
      )}
      {done === "reported" && (
        <p className="notice" role="status">
          {t.report.thanks}
        </p>
      )}

      <div className="detail">
        <div>
          <section className="card" aria-labelledby="map-title" id="map">
            <h2 id="map-title">{t.map.title}</h2>
            {p.pin ? (
              <>
                <iframe
                  className="map-frame"
                  title={t.map.frameTitle}
                  loading="lazy"
                  referrerPolicy="no-referrer"
                  src={`https://www.openstreetmap.org/export/embed.html?bbox=${p.pin.longitude - 0.01},${p.pin.latitude - 0.006},${p.pin.longitude + 0.01},${p.pin.latitude + 0.006}&layer=mapnik&marker=${p.pin.latitude},${p.pin.longitude}`}
                />
                <p className="form-inline">
                  <a className="button-link primary" href={`https://www.google.com/maps/dir/?api=1&destination=${p.pin.latitude},${p.pin.longitude}`} rel="noopener noreferrer" target="_blank">
                    {t.map.directions}
                    <span className="visually-hidden"> {t.nearby.opensNewTab}</span>
                  </a>
                </p>
                <p className="hint">
                  {(t.map.sources as Record<string, string>)[p.pin.source]} · {t.map.note} © OpenStreetMap
                </p>
              </>
            ) : (
              <p className="muted">{t.map.none}</p>
            )}
            {mayPin && (
              <SetPinForm
                locale={locale}
                parcelId={id}
                labels={{ ...t.map.form, title: p.pin ? t.map.form.titleChange : t.map.form.title }}
              />
            )}
          </section>

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

          <section className="card" aria-labelledby="nearby">
            <h2 id="nearby">{t.nearby.title}</h2>
            {!nearby.ok || nearby.data.length === 0 ? (
              <p className="muted">{t.nearby.none}</p>
            ) : (
              <div className="table-wrap" tabIndex={0} role="region" aria-labelledby="nearby">
                <table className="facts nearby">
                  <thead>
                    <tr>
                      <th scope="col">{t.nearby.project}</th>
                      <th scope="col">{t.nearby.distance}</th>
                      <th scope="col">{t.nearby.status}</th>
                      <th scope="col">{t.nearby.source}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {nearby.data.map((d) => (
                      <tr key={d.projectId}>
                        <td>
                          <strong>{d.name}</strong>
                          <br />
                          <span className="muted">{(t.nearby.kinds as Record<string, string>)[d.kind] ?? d.kind}</span>
                          {d.mayAffectParcel && <p className="warning-text">{t.nearby.mayAffect}</p>}
                        </td>
                        <td>{d.distanceKm === null ? l.notGiven : format(t.nearby.km, { km: n(d.distanceKm, 1) })}</td>
                        <td>{(t.nearby.statuses as Record<string, string>)[d.status] ?? d.status}</td>
                        <td>
                          {d.sourceUrl ? (
                            <a href={d.sourceUrl} rel="noopener noreferrer nofollow" target="_blank">
                              {d.authority}
                              <span className="visually-hidden"> {t.nearby.opensNewTab}</span>
                            </a>
                          ) : (
                            d.authority
                          )}
                          <br />
                          <span className="muted">{new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" }).format(new Date(d.sourceDate))}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="hint">{t.nearby.note}</p>
          </section>

          {p.pin && (
            <section className="card" aria-labelledby="compare-nearby">
              <h2 id="compare-nearby">{nearbyMode === "rent" ? t.compareNearby.rentTitle : t.compareNearby.landTitle}</h2>
              <nav className="pills" aria-label={t.compareNearby.switchLabel}>
                <Link href={`/${locale}/parcels/${id}?nearby=land#compare-nearby`} className={nearbyMode === "land" ? "active" : undefined} aria-current={nearbyMode === "land" ? "page" : undefined}>
                  {t.compareNearby.landTab}
                </Link>
                <Link href={`/${locale}/parcels/${id}?nearby=rent#compare-nearby`} className={nearbyMode === "rent" ? "active" : undefined} aria-current={nearbyMode === "rent" ? "page" : undefined}>
                  {t.compareNearby.rentTab}
                </Link>
              </nav>
              {nearbyMode === "land" ? (
                !landComparison?.ok || landComparison.data.items.length === 0 ? (
                  <p className="muted">{t.compareNearby.noLand}</p>
                ) : (
                  <>
                    <div className="table-wrap" tabIndex={0} role="region" aria-labelledby="compare-nearby">
                      <table className="compare-table">
                        <thead>
                          <tr>
                            <th scope="col">{t.compareNearby.land}</th>
                            <th scope="col">{t.compareNearby.area}</th>
                            <th scope="col">{t.compareNearby.price}</th>
                            <th scope="col">{t.compareNearby.perSqYd}</th>
                            <th scope="col">{t.compareNearby.perGunta}</th>
                            <th scope="col">{t.compareNearby.road}</th>
                            <th scope="col">{t.compareNearby.prosCons}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {[landComparison.data.subject, ...landComparison.data.items].map((c, index) => (
                            <tr key={c.id} className={index === 0 ? "row-subject" : undefined}>
                              <th scope="row">
                                {index === 0 ? (
                                  <strong>{t.compareNearby.thisLand}</strong>
                                ) : (
                                  <Link href={`/${locale}/parcels/${c.id}`}>
                                    {c.village}, {c.mandal}
                                  </Link>
                                )}
                                <br />
                                <span className="muted">
                                  {(s.landClasses as Record<string, string>)[c.landClass] ?? c.landClass}
                                  {c.distanceKm !== null && <> · {format(t.nearby.km, { km: n(c.distanceKm, 1) })}</>}
                                </span>
                              </th>
                              <td>
                                {format(t.compareNearby.sqYd, { value: n(c.areaSquareYards, 0) })}
                                <br />
                                <span className="muted">{format(t.compareNearby.guntas, { value: n(c.areaGuntas, 2) })}</span>
                              </td>
                              <td>{c.lowestPriceRupees === null ? t.parcels.noPrice : formatRupeesShort(c.lowestPriceRupees, intl, t.amounts)}</td>
                              <td>{c.pricePerSquareYard === null ? "–" : formatRupees(c.pricePerSquareYard, intl)}</td>
                              <td>{c.pricePerGunta === null ? "–" : formatRupeesShort(c.pricePerGunta, intl, t.amounts)}</td>
                              <td>{c.roadWidthM === null ? "–" : format(t.compareNearby.roadValue, { feet: n(c.roadWidthM * 3.28084, 0) })}</td>
                              <td>
                                {index === 0 ? (
                                  <span className="muted">{t.compareNearby.baseline}</span>
                                ) : (
                                  <PointsList
                                    points={c.points}
                                    texts={t.compareNearby.points as Record<string, string>}
                                    unit={t.compareNearby.unitSqYd}
                                    kinds={s.landClasses as Record<string, string>}
                                    furnishing={t.rent.furnishings as Record<string, string>}
                                    words={t.compareNearby.words}
                                  />
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    <p className="hint">{t.compareNearby.landNote}</p>
                  </>
                )
              ) : !nearbyRentals?.ok || nearbyRentals.data.length === 0 ? (
                <p className="muted">{t.compareNearby.noRent}</p>
              ) : (
                <>
                  <ul className="parcel-list">
                    {nearbyRentals.data.map((h) => (
                      <li key={h.id} className="result">
                        <h3>
                          <Link href={`/${locale}/rentals/${h.id}`}>
                            {(h.bedrooms === 0 ? ((t.rent.types as Record<string, string>)[h.propertyType] ?? h.propertyType) : format(t.rent.headline, { bedrooms: h.bedrooms, type: (t.rent.types as Record<string, string>)[h.propertyType] ?? h.propertyType }))}, {h.locality}
                          </Link>
                        </h3>
                        <p className="price">
                          {format(t.rent.perMonth, { rent: formatRupees(h.rentRupees, intl) })}{" "}
                          <span className="muted">· {format(t.rent.perSqft, { value: formatRupees(h.rentPerSqft, intl) })}</span>
                        </p>
                        <p className="muted">
                          {format(t.rent.sqft, { value: n(h.areaSqft, 0) })} · {(t.rent.furnishings as Record<string, string>)[h.furnishing]}
                          {h.distanceKm !== null && <> · {format(t.nearby.km, { km: n(h.distanceKm, 1) })}</>}
                        </p>
                      </li>
                    ))}
                  </ul>
                  <p className="hint">{t.compareNearby.rentNote}</p>
                </>
              )}
              <p>
                <Link href={`/${locale}/map${nearbyMode === "rent" ? "?mode=rent" : ""}`}>{t.landMap.openMap}</Link>
              </p>
            </section>
          )}

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
            {involved && <Link href={`/${locale}/parcels/${id}/correct`}>{l.correct}</Link>}
            <Link href={`/${locale}/parcels/${id}/report`}>{t.report.link}</Link>
            <p className="safety">{t.home.safety}</p>
          </section>
        </aside>
      </div>
    </section>
  );
}
