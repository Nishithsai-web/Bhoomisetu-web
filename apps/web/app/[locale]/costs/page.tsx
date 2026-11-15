import { notFound } from "next/navigation";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";
import { CostCalculator } from "@/components/CostCalculator";

export default async function CostsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale).costs;

  return (
    <section className="page">
      <h1>{t.title}</h1>
      <p className="lead">{t.intro}</p>
      <CostCalculator
        intlLocale={INTL_LOCALE[locale]}
        labels={{
          price: t.price,
          state: t.state,
          stateTG: t.stateTG,
          stateAP: t.stateAP,
          area: t.area,
          urban: t.urban,
          rural: t.rural,
          loanToggle: t.loanToggle,
          loanAmount: t.loanAmount,
          loanRate: t.loanRate,
          loanYears: t.loanYears,
          invalid: t.invalid,
          duties: t.duties,
          stamp: t.stamp,
          transfer: t.transfer,
          registration: t.registration,
          dutiesTotal: t.dutiesTotal,
          upfront: t.upfront,
          loanHeading: t.loanHeading,
          emi: t.emi,
          totalInterest: t.totalInterest,
          totalPaid: t.totalPaid,
          liveSummary: t.liveSummary,
          sourceNote: t.sourceNote,
        }}
      />
    </section>
  );
}
