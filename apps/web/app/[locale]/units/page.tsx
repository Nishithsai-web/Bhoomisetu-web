import { notFound } from "next/navigation";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";
import { AreaConverter } from "@/components/AreaConverter";

export default async function UnitsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale).units;

  return (
    <section className="page">
      <h1>{t.title}</h1>
      <p className="lead">{t.intro}</p>
      <AreaConverter
        intlLocale={INTL_LOCALE[locale]}
        labels={{ value: t.value, unit: t.unit, invalid: t.invalid, results: t.results, liveSummary: t.liveSummary, names: t.names }}
      />
      <p className="muted">{t.note}</p>
    </section>
  );
}
