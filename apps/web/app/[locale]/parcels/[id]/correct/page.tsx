import { notFound, redirect } from "next/navigation";
import { CorrectionForm } from "@/components/CorrectionForm";
import { isUuid } from "@/lib/api";
import { getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken } from "@/lib/session";

export default async function CorrectPage({ params }: { params: Promise<{ locale: string; id: string }> }) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  if (!(await getSessionToken())) redirect(`/${locale}/signin?next=/${locale}/parcels/${id}/correct`);
  const t = getDictionary(locale);

  return (
    <section className="page narrow">
      <h1>{t.correct.title}</h1>
      <p className="lead">{t.correct.intro}</p>
      <CorrectionForm locale={locale} parcelId={id} t={t} errors={t.errors as Record<string, string>} />
    </section>
  );
}
