import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { ReportForm } from "@/components/ReportForm";
import { REPORT_REASONS, getParcelPlace, isUuid } from "@/lib/api";
import { getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken } from "@/lib/session";

type Props = { params: Promise<{ locale: string; id: string }> };

export default async function ReportPage({ params }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  const t = getDictionary(locale);
  if (!(await getSessionToken())) redirect(`/${locale}/signin?next=${encodeURIComponent(`/${locale}/parcels/${id}/report`)}`);
  const place = await getParcelPlace(id);
  if (!place.ok && place.status === 404) notFound();

  return (
    <section className="page narrow">
      <p>
        <Link href={`/${locale}/parcels/${id}`}>{t.land.backToLand}</Link>
      </p>
      <h1>{t.report.title}</h1>
      {place.ok && (
        <p className="lead">
          {place.data.location.village}, {place.data.location.mandal}, {place.data.location.district} · {place.data.location.surveyNumber}
        </p>
      )}
      <p>{t.report.intro}</p>
      <ReportForm locale={locale} parcelId={id} reasons={REPORT_REASONS} t={t} />
    </section>
  );
}
