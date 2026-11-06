import { notFound } from "next/navigation";
import { getDictionary, isLocale } from "@/lib/i18n";

export default async function HomePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale).home;

  const features = [
    { title: t.verifiedTitle, body: t.verifiedBody },
    { title: t.compareTitle, body: t.compareBody },
    { title: t.unitsTitle, body: t.unitsBody },
    { title: t.languageTitle, body: t.languageBody },
  ];

  return (
    <>
      <section className="hero">
        <h1>{t.heroTitle}</h1>
        <p className="lead">{t.heroSubtitle}</p>
        <form action={`/${locale}/parcels`} method="get" role="search" className="search">
          <label htmlFor="q">{t.searchLabel}</label>
          <div className="search-row">
            <input id="q" name="q" type="search" placeholder={t.searchPlaceholder} maxLength={100} autoComplete="off" />
            <button type="submit">{t.searchButton}</button>
          </div>
        </form>
        <p className="safety" role="note">
          {t.safety}
        </p>
      </section>
      <section className="features" aria-label={t.heroTitle}>
        {features.map((feature) => (
          <article key={feature.title} className="card">
            <h2>{feature.title}</h2>
            <p>{feature.body}</p>
          </article>
        ))}
      </section>
    </>
  );
}
