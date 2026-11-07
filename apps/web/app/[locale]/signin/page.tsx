import { notFound } from "next/navigation";
import { SignInForm } from "@/components/SignInForm";
import { safeNextPath } from "@/lib/forms";
import { getDictionary, isLocale } from "@/lib/i18n";

type Props = { params: Promise<{ locale: string }>; searchParams: Promise<{ next?: string | string[] }> };

export default async function SignInPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);
  const raw = (await searchParams).next;
  const next = safeNextPath(Array.isArray(raw) ? raw[0] : raw, `/${locale}`);

  return (
    <section className="page narrow">
      <h1>{t.signin.title}</h1>
      <p className="lead">{t.signin.intro}</p>
      <SignInForm locale={locale} next={next} labels={{ ...t.signin, sending: t.form.sending }} errors={t.errors as Record<string, string>}
        turnstileSiteKey={process.env.BHOOMISETU_TURNSTILE_SITE_KEY || undefined}
      />
    </section>
  );
}
