import Link from "next/link";
import { notFound } from "next/navigation";
import { contactRentalAction } from "../../actions";
import { PointsList } from "@/components/PointsList";
import { getRental, getRentalComparison, isUuid } from "@/lib/api";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { formatNumber, formatRupees } from "@/lib/money";
import { getSessionToken } from "@/lib/session";

type Props = { params: Promise<{ locale: string; id: string }>; searchParams: Promise<{ done?: string; error?: string }> };

/** One home for rent: rent, deposit, size, map, WhatsApp contact, and a comparison with homes nearby. */
export default async function RentalPage({ params, searchParams }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  const r = t.rent;
  const intl = INTL_LOCALE[locale];
  const { done, error } = await searchParams;
  const token = (await getSessionToken()) ?? undefined;
  const [home, comparison] = await Promise.all([getRental(id, token), getRentalComparison(id)]);
  if (!home.ok) {
    if (home.status === 404) notFound();
    return (
      <section className="page narrow">
        <p className="notice error" role="alert">
          {home.status === 410 ? r.gone : errorText(t, home.code)}
        </p>
        <p>
          <Link href={`/${locale}/map?mode=rent`}>{r.findMore}</Link>
        </p>
      </section>
    );
  }

  const h = home.data;
  const types = r.types as Record<string, string>;
  const furnishings = r.furnishings as Record<string, string>;
  const n = (value: number, digits = 0) => formatNumber(value, intl, digits);
  const date = new Intl.DateTimeFormat(intl, { day: "numeric", month: "short", year: "numeric" });
  const headline = (bedrooms: number, type: string) =>
    bedrooms === 0 ? (types[type] ?? type) : format(r.headline, { bedrooms, type: types[type] ?? type });

  return (
    <section className="page">
      <p>
        <Link href={`/${locale}/map?mode=rent`}>{r.backToMap}</Link>
      </p>
      {done === "listed" && (
        <p className="notice" role="status">
          {r.listed}
        </p>
      )}
      {error && (
        <p className="notice error" role="alert">
          {errorText(t, error)}
        </p>
      )}
      <ul className="tags">
        <li className="tag tag-class">{types[h.propertyType] ?? h.propertyType}</li>
        <li className="tag tag-plain">{h.listerKind === "owner" ? r.byOwner : r.byAgent}</li>
      </ul>
      <h1>
        {headline(h.bedrooms, h.propertyType)}, {h.locality}
      </h1>
      {h.stale && <p className="notice">{r.staleNotice}</p>}
      <p className="lead">
        {format(r.perMonth, { rent: formatRupees(h.rentRupees, intl) })} · {format(r.perSqft, { value: formatRupees(h.rentPerSqft, intl) })}
      </p>

      <div className="detail">
        <div>
          <section className="card" aria-labelledby="home-facts">
            <h2 id="home-facts">{r.factsTitle}</h2>
            <table className="facts">
              <tbody>
                <tr>
                  <th scope="row">{r.deposit}</th>
                  <td>
                    {formatRupees(h.depositRupees, intl)} <span className="muted">({format(r.depositMonths, { months: n(h.depositMonths, 1) })})</span>
                  </td>
                </tr>
                <tr>
                  <th scope="row">{r.area}</th>
                  <td>{format(r.sqft, { value: n(h.areaSqft) })}</td>
                </tr>
                <tr>
                  <th scope="row">{r.furnishing}</th>
                  <td>{furnishings[h.furnishing] ?? h.furnishing}</td>
                </tr>
                <tr>
                  <th scope="row">{r.tenants}</th>
                  <td>{(r.tenantKinds as Record<string, string>)[h.preferredTenants] ?? h.preferredTenants}</td>
                </tr>
                <tr>
                  <th scope="row">{r.parking}</th>
                  <td>{h.parking ? r.yes : r.no}</td>
                </tr>
                <tr>
                  <th scope="row">{r.availableFrom}</th>
                  <td>{date.format(new Date(h.availableFrom))}</td>
                </tr>
                <tr>
                  <th scope="row">{r.place}</th>
                  <td>
                    {h.locality}, {h.mandal}, {h.district}
                  </td>
                </tr>
                <tr>
                  <th scope="row">{r.confirmed}</th>
                  <td>{date.format(new Date(h.availabilityConfirmedAt))}</td>
                </tr>
              </tbody>
            </table>
          </section>

          <section className="card" aria-labelledby="home-map">
            <h2 id="home-map">{t.map.title}</h2>
            <iframe
              className="map-frame"
              title={t.map.frameTitle}
              loading="lazy"
              referrerPolicy="no-referrer"
              src={`https://www.openstreetmap.org/export/embed.html?bbox=${h.longitude - 0.01},${h.latitude - 0.006},${h.longitude + 0.01},${h.latitude + 0.006}&layer=mapnik${h.approximate ? "" : `&marker=${h.latitude},${h.longitude}`}`}
            />
            <p className="hint">
              {h.approximate ? r.approximateArea : t.map.note} © OpenStreetMap
            </p>
          </section>

          <section className="card" aria-labelledby="compare-homes">
            <h2 id="compare-homes">{t.compareNearby.homesTitle}</h2>
            {!comparison.ok || comparison.data.items.length === 0 ? (
              <p className="muted">{t.compareNearby.noRent}</p>
            ) : (
              <div className="table-wrap" tabIndex={0} role="region" aria-labelledby="compare-homes">
                <table className="compare-table">
                  <thead>
                    <tr>
                      <th scope="col">{r.home}</th>
                      <th scope="col">{r.rent}</th>
                      <th scope="col">{r.rentPerSqft}</th>
                      <th scope="col">{r.area}</th>
                      <th scope="col">{t.compareNearby.prosCons}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {[comparison.data.subject, ...comparison.data.items].map((c, index) => (
                      <tr key={c.id} className={index === 0 ? "row-subject" : undefined}>
                        <th scope="row">
                          {index === 0 ? (
                            <strong>{r.thisHome}</strong>
                          ) : (
                            <Link href={`/${locale}/rentals/${c.id}`}>
                              {headline(c.bedrooms, c.propertyType)}, {c.locality}
                            </Link>
                          )}
                          {c.distanceKm !== null && (
                            <>
                              <br />
                              <span className="muted">{format(t.nearby.km, { km: n(c.distanceKm, 1) })}</span>
                            </>
                          )}
                        </th>
                        <td>{formatRupees(c.rentRupees, intl)}</td>
                        <td>{formatRupees(c.rentPerSqft, intl)}</td>
                        <td>{format(r.sqft, { value: n(c.areaSqft) })}</td>
                        <td>
                          {index === 0 ? (
                            <span className="muted">{t.compareNearby.baseline}</span>
                          ) : (
                            <PointsList
                              points={c.points}
                              texts={t.compareNearby.points as Record<string, string>}
                              unit={t.compareNearby.unitSqFt}
                              kinds={types}
                              furnishing={furnishings}
                              words={t.compareNearby.words}
                            />
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="hint">{t.compareNearby.rentNote}</p>
          </section>
        </div>

        <aside className="stack">
          <section className="card stack">
            <h2>{r.contactTitle}</h2>
            {h.contactable && !h.mine ? (
              <form action={contactRentalAction}>
                <input type="hidden" name="locale" value={locale} />
                <input type="hidden" name="rentalId" value={h.id} />
                <button type="submit" className="whatsapp">
                  {t.contact.whatsapp}
                </button>
              </form>
            ) : (
              <p className="muted">{h.mine ? r.yourHome : h.stale ? r.staleNotice : t.errors["contact.lister_not_subscribed"]}</p>
            )}
            <p className="safety">{r.safety}</p>
          </section>
        </aside>
      </div>
    </section>
  );
}
