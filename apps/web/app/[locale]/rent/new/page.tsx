import { notFound, redirect } from "next/navigation";
import { RentalForm } from "@/components/RentalForm";
import { FURNISHINGS, PROPERTY_TYPES, TENANTS } from "@/lib/api";
import { getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken } from "@/lib/session";

export default async function NewRentalPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  if (!(await getSessionToken())) redirect(`/${locale}/signin?next=${encodeURIComponent(`/${locale}/rent/new`)}`);
  const t = getDictionary(locale);
  return (
    <section className="page narrow">
      <h1>{t.rent.newTitle}</h1>
      <p className="lead">{t.rent.newIntro}</p>
      <RentalForm locale={locale} t={t} options={{ propertyTypes: PROPERTY_TYPES, furnishings: FURNISHINGS, tenants: TENANTS }} />
    </section>
  );
}
