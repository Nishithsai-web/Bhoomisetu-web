import { notFound, redirect } from "next/navigation";
import { ParcelForm } from "@/components/ParcelForm";
import { getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken } from "@/lib/session";

export default async function SellPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  if (!(await getSessionToken())) redirect(`/${locale}/signin?next=/${locale}/sell`);
  const t = getDictionary(locale);

  return (
    <section className="page narrow">
      <h1>{t.sell.title}</h1>
      <p className="lead">{t.sell.intro}</p>
      <ParcelForm locale={locale} t={t.sell} unitNames={t.units.names} form={t.form} errors={t.errors as Record<string, string>} />
    </section>
  );
}
