import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Inter, Noto_Sans_Devanagari, Noto_Sans_Telugu } from "next/font/google";
import Link from "next/link";
import { LOCALES, getDictionary, isLocale } from "@/lib/i18n";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";
import "../globals.css";

const inter = Inter({ subsets: ["latin"], variable: "--font-latin", display: "swap" });
const telugu = Noto_Sans_Telugu({ subsets: ["telugu"], variable: "--font-telugu", display: "swap" });
const devanagari = Noto_Sans_Devanagari({ subsets: ["devanagari"], variable: "--font-devanagari", display: "swap" });

type Props = { children: React.ReactNode; params: Promise<{ locale: string }> };

// Only /en, /te and /hi exist; any other first segment is a 404 before the layout renders.
export const dynamicParams = false;

export function generateStaticParams() {
  return LOCALES.map((locale) => ({ locale }));
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { locale } = await params;
  if (!isLocale(locale)) return {};
  const t = getDictionary(locale);
  return {
    title: t.meta.title,
    description: t.meta.description,
    alternates: { languages: { en: "/en", te: "/te", hi: "/hi" } },
  };
}

export default async function LocaleLayout({ children, params }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);

  return (
    <html lang={locale} className={`${inter.variable} ${telugu.variable} ${devanagari.variable}`}>
      <body>
        <a className="skip-link" href="#main">
          {t.nav.skipToContent}
        </a>
        <header className="site-header">
          <Link href={`/${locale}`} className="brand">
            {locale === "en" ? "Bhoomisetu" : locale === "te" ? "భూమిసేతు" : "भूमिसेतु"}
          </Link>
          <nav aria-label="Main" className="main-nav">
            <Link href={`/${locale}/parcels`}>{t.nav.parcels}</Link>
            <Link href={`/${locale}/units`}>{t.nav.units}</Link>
            <Link href={`/${locale}/sell`}>{t.nav.sell}</Link>
          </nav>
          <LanguageSwitcher current={locale} label={t.nav.language} />
        </header>
        <main id="main">{children}</main>
        <footer className="site-footer">
          <p>{t.footer.disclaimer}</p>
        </footer>
      </body>
    </html>
  );
}
