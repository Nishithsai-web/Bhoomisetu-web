import { notFound, redirect } from "next/navigation";
import { getOwnedAgentOffers } from "@/lib/api";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";
import { getSessionToken, clearSession } from "@/lib/session";
import { ownerAgentAction } from "../actions";

type Props = { params: Promise<{ locale: string }>; searchParams: Promise<{ done?: string; error?: string }> };

export default async function OwnerAgentsPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/agents`);

  const t = getDictionary(locale);
  const a = t.ownerAgents;
  const query = await searchParams;
  const offers = await getOwnedAgentOffers(token);
  if (!offers.ok && offers.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/agents`);
  }
  const dateFormat = new Intl.DateTimeFormat(INTL_LOCALE[locale], { day: "numeric", month: "short", year: "numeric" });

  return (
    <section className="page">
      <h1>{a.title}</h1>
      <p className="lead">{a.intro}</p>
      {query.done && a.done[query.done as "confirmed" | "revoked"] && <p className="notice" role="status">{a.done[query.done as "confirmed" | "revoked"]}</p>}
      {query.error && <p className="error" role="alert">{errorText(t, query.error)}</p>}
      {!offers.ok && <p className="error" role="alert">{errorText(t, offers.code)}</p>}
      {offers.ok && offers.data.length === 0 && <p>{a.empty}</p>}
      {offers.ok &&
        offers.data.map((offer) => (
          <article key={offer.offerId} className="card">
            <h2>
              {offer.village}, {offer.mandal}
            </h2>
            <p className="muted">
              {offer.district} · {t.parcels.survey.replace("{number}", offer.surveyNumber)} · {a.listed}: {dateFormat.format(new Date(offer.listedAt))}
            </p>
            <p>
              <strong>{offer.confirmed ? a.confirmed : a.notConfirmed}</strong>
            </p>
            <form action={ownerAgentAction}>
              <input type="hidden" name="locale" value={locale} />
              <input type="hidden" name="parcelId" value={offer.parcelId} />
              <input type="hidden" name="offerId" value={offer.offerId} />
              {offer.confirmed ? (
                <button type="submit" name="intent" value="revoke" className="link-button">
                  {a.revoke}
                </button>
              ) : (
                <button type="submit" name="intent" value="confirm">
                  {a.confirm}
                </button>
              )}
            </form>
          </article>
        ))}
    </section>
  );
}
