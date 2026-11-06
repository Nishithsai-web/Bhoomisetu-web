import { notFound, redirect } from "next/navigation";
import { OfferForm } from "@/components/OfferForm";
import { isUuid } from "@/lib/api";
import { getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken } from "@/lib/session";

type Props = {
  params: Promise<{ locale: string; id: string }>;
  searchParams: Promise<{ existing?: string; new?: string }>;
};

export default async function OfferPage({ params, searchParams }: Props) {
  const { locale, id } = await params;
  if (!isLocale(locale) || !isUuid(id)) notFound();
  if (!(await getSessionToken())) redirect(`/${locale}/signin?next=/${locale}/parcels/${id}/offer`);
  const t = getDictionary(locale);
  const query = await searchParams;

  return (
    <section className="page narrow">
      <h1>{t.offer.title}</h1>
      {query.existing === "1" && (
        <p className="notice" role="status">
          {t.offer.existing}
        </p>
      )}
      {query.new === "1" && (
        <p className="notice" role="status">
          {t.offer.created}
        </p>
      )}
      <OfferForm locale={locale} parcelId={id} t={t.offer} errors={t.errors as Record<string, string>} />
    </section>
  );
}
