import Link from "next/link";
import { notFound } from "next/navigation";
import { getDictionary, isLocale } from "@/lib/i18n";

const TYPES = ["agricultural", "non_agricultural", "converted"] as const;

/** Home: search first, then land types, what a badge means, units and safe contact. Every link goes to a working page. */
export default async function HomePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);
  const h = t.home;

  return (
    <>
      <section className="band">
        <h1>{h.heroTitle}</h1>
        <p className="lead">{h.heroSubtitle}</p>
        <form action={`/${locale}/parcels`} method="get" role="search" className="search-card">
          <label htmlFor="q">{h.searchLabel}</label>
          <div className="search-row">
            <input id="q" name="q" type="search" placeholder={h.searchPlaceholder} maxLength={100} autoComplete="off" aria-describedby="q-examples" />
            <button type="submit">{h.searchButton}</button>
          </div>
          <p id="q-examples" className="examples">
            {h.examples}
          </p>
        </form>
      </section>

      <section className="section" aria-labelledby="explore">
        <h2 id="explore">{h.exploreTitle}</h2>
        <div className="type-grid">
          {TYPES.map((type) => (
            <Link key={type} href={`/${locale}/parcels?landClass=${type}`} className="type-card">
              <strong>{h.types[type].title}</strong>
              <span>{h.types[type].body}</span>
            </Link>
          ))}
          <Link href={`/${locale}/parcels`} className="type-card">
            <strong>{h.types.all.title}</strong>
            <span>{h.types.all.body}</span>
          </Link>
        </div>
      </section>

      <section className="section split">
        <div className="panel-dark">
          <h2>{h.checksTitle}</h2>
          <ul>
            {Object.values(h.checksList).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
          <p className="small">{h.checksNote}</p>
        </div>
        <div className="card">
          <h2>{h.sellTitle}</h2>
          <p className="muted">{h.sellBody}</p>
          <Link href={`/${locale}/sell`} className="big-link">
            {h.sellLink}
          </Link>
        </div>
      </section>

      <section className="section split">
        <div>
          <h2>{h.unitsTitle}</h2>
          <p className="muted">{h.unitsBody}</p>
          <Link href={`/${locale}/units`} className="big-link">
            {h.unitsLink}
          </Link>
        </div>
        <div>
          <h2>{h.contactTitle}</h2>
          <p className="muted">{h.contactBody}</p>
        </div>
      </section>

      <p className="safety" role="note">
        {h.safety}
      </p>
    </>
  );
}
