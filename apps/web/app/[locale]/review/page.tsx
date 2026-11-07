import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { createDevelopmentAction, decideChangeAction, decideDocumentAction, decideReportAction, linkDevelopmentAction, mergeDuplicateAction } from "../actions";
import { ActivateSubscriptionForm } from "@/components/ActivateSubscriptionForm";
import { ConfirmBox } from "@/components/ConfirmBox";
import { SetOwnerForm } from "@/components/SetOwnerForm";
import { DEVELOPMENT_KINDS, DEVELOPMENT_STATUSES, getChangeRequestQueue, getDevelopmentProjects, getDocumentQueue, getDuplicateQueue, getMe, getReportQueue } from "@/lib/api";
import { formatFileSize } from "@/lib/contact";
import { errorText } from "@/lib/errors";
import { INTL_LOCALE, format, getDictionary, isLocale } from "@/lib/i18n";
import { clearSession, getSessionToken } from "@/lib/session";

type Props = { params: Promise<{ locale: string }>; searchParams: Promise<{ done?: string; error?: string; merge?: string }> };

/** The trust reviewer's console: correction requests, set-aside duplicates, and recording an owner. */
export default async function ReviewPage({ params, searchParams }: Props) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const token = await getSessionToken();
  if (!token) redirect(`/${locale}/signin?next=/${locale}/review`);
  const t = getDictionary(locale);
  const r = t.review;
  const query = await searchParams;

  const me = await getMe(token);
  if (!me.ok && me.status === 401) {
    await clearSession();
    redirect(`/${locale}/signin?next=/${locale}/review`);
  }
  if (!me.ok || !me.data.isTrustReviewer) {
    return (
      <section className="page narrow">
        <h1>{r.title}</h1>
        <p className="notice">{r.notReviewer}</p>
      </section>
    );
  }

  const [changes, duplicates, documents, reports, projects] = await Promise.all([
    getChangeRequestQueue(token),
    getDuplicateQueue(token),
    getDocumentQueue(token),
    getReportQueue(token),
    getDevelopmentProjects(token),
  ]);
  const rt = t.reviewTools;
  const x = t.reviewExtra;
  const docTypes = t.documents.types as Record<string, string>;
  const intl = INTL_LOCALE[locale];
  const done = query.done && Object.hasOwn(r.done, query.done) ? r.done[query.done as keyof typeof r.done] : null;
  const fieldName = (field: string) => (Object.hasOwn(t.correct.fields, field) ? t.correct.fields[field as keyof typeof t.correct.fields] : field);

  return (
    <section className="page">
      <h1>{r.title}</h1>
      <p className="lead">{r.intro}</p>
      {done && (
        <p className="notice" role="status">
          {done}
        </p>
      )}
      {query.error && (
        <p className="notice error" role="alert">
          {errorText(t, query.error)}
        </p>
      )}

      <h2>{r.corrections}</h2>
      {!changes.ok ? (
        <p className="notice">{errorText(t, changes.code)}</p>
      ) : changes.data.length === 0 ? (
        <p className="notice">{r.nothingWaiting}</p>
      ) : (
        <ul className="parcel-list">
          {changes.data.map((c) => (
            <li key={c.id} className="card">
              <p>
                <strong>{fieldName(c.field)}</strong>: {c.currentValue ?? "—"} → <strong>{c.proposedValue}</strong>
              </p>
              <p className="hint">“{c.reason}”</p>
              <p>
                <Link href={`/${locale}/parcels/${c.parcelId}/compare`}>{t.parcels.compare}</Link>
              </p>
              <form action={decideChangeAction} className="form">
                <input type="hidden" name="locale" value={locale} />
                <input type="hidden" name="requestId" value={c.id} />
                <label htmlFor={`note-${c.id}`}>{r.note}</label>
                <input id={`note-${c.id}`} name="note" maxLength={1000} />
                <div className="actions">
                  <button type="submit" name="decision" value="approve">
                    {r.approve}
                  </button>
                  <button type="submit" name="decision" value="reject" className="secondary">
                    {r.reject}
                  </button>
                </div>
              </form>
            </li>
          ))}
        </ul>
      )}

      <h2>{x.documentsTitle}</h2>
      <p className="hint">{x.documentRule}</p>
      {!documents.ok ? (
        <p className="notice">{errorText(t, documents.code)}</p>
      ) : documents.data.length === 0 ? (
        <p className="notice">{r.nothingWaiting}</p>
      ) : (
        <ul className="parcel-list">
          {documents.data.map((doc) => (
            <li key={doc.id} className="card">
              <p>
                <strong>{docTypes[doc.type] ?? doc.type}</strong>
                {doc.referenceNumber && <span className="muted"> · {t.documents.referencePrefix} {doc.referenceNumber}</span>}
              </p>
              <p className="hint">
                {doc.village}, {doc.mandal}, {doc.district} · {doc.surveyNumber} · {formatFileSize(doc.sizeBytes, intl)}
              </p>
              <p>
                <a href={`/files/${doc.parcelId}/${doc.id}?l=${locale}`} target="_blank" rel="noopener">
                  {x.openFile}
                </a>
              </p>
              <form action={decideDocumentAction} className="form">
                <input type="hidden" name="locale" value={locale} />
                <input type="hidden" name="documentId" value={doc.id} />
                <label htmlFor={`doc-note-${doc.id}`}>{r.note}</label>
                <input id={`doc-note-${doc.id}`} name="note" maxLength={1000} />
                <div className="actions">
                  <button type="submit" name="decision" value="approve">
                    {r.approve}
                  </button>
                  <button type="submit" name="decision" value="reject" className="secondary">
                    {r.reject}
                  </button>
                </div>
              </form>
            </li>
          ))}
        </ul>
      )}

      <h2>{rt.reportsTitle}</h2>
      {!reports.ok ? (
        <p className="notice">{errorText(t, reports.code)}</p>
      ) : reports.data.length === 0 ? (
        <p className="notice">{r.nothingWaiting}</p>
      ) : (
        <ul className="parcel-list">
          {reports.data.map((rep) => (
            <li key={rep.id} className="card">
              <p>
                <strong>{(t.report.reasons as Record<string, string>)[rep.reason] ?? rep.reason}</strong> ·{" "}
                <Link href={`/${locale}/parcels/${rep.parcelId}`}>
                  {rep.village}, {rep.mandal}, {rep.district} · {rep.surveyNumber}
                </Link>
              </p>
              {rep.details && <p className="hint">{rep.details}</p>}
              <form action={decideReportAction} className="form">
                <input type="hidden" name="locale" value={locale} />
                <input type="hidden" name="reportId" value={rep.id} />
                <label htmlFor={`rep-note-${rep.id}`}>{r.note}</label>
                <input id={`rep-note-${rep.id}`} name="note" maxLength={1000} required />
                <div className="actions">
                  <button type="submit" name="decision" value="resolve">
                    {rt.resolve}
                  </button>
                  <button type="submit" name="decision" value="dismiss" className="secondary">
                    {rt.dismiss}
                  </button>
                </div>
              </form>
            </li>
          ))}
        </ul>
      )}

      <h2>{r.duplicates}</h2>
      {!duplicates.ok ? (
        <p className="notice">{errorText(t, duplicates.code)}</p>
      ) : duplicates.data.length === 0 ? (
        <p className="notice">{r.nothingWaiting}</p>
      ) : (
        <ul className="parcel-list">
          {duplicates.data.map((d) => (
            <li key={d.parcelId} className="card">
              <p>
                {d.village}, {d.mandal}, {d.district} · {d.surveyNumber}
              </p>
              <p className="hint">{r.duplicateOffers.replace("{count}", String(d.activeOffers))}</p>
              <p>
                <Link href={`/${locale}/parcels/${d.keptParcelId}/compare`}>{r.keptRecord}</Link>
              </p>
              {/* Merging cannot be undone, so it takes a second, explicit step. */}
              {query.merge === d.parcelId ? (
                <ConfirmBox id={`merge-${d.parcelId}`} question={r.mergeConfirm}>
                  <form action={mergeDuplicateAction} className="form-inline">
                    <input type="hidden" name="locale" value={locale} />
                    <input type="hidden" name="parcelId" value={d.parcelId} />
                    <button type="submit">{r.mergeYes}</button>{" "}
                    <Link href={`/${locale}/review`}>{r.mergeNo}</Link>
                  </form>
                </ConfirmBox>
              ) : (
                <p>
                  <Link className="button-link" href={`/${locale}/review?merge=${d.parcelId}`}>
                    {r.merge}
                  </Link>
                </p>
              )}
            </li>
          ))}
        </ul>
      )}

      <h2>{r.setOwner}</h2>
      <SetOwnerForm locale={locale} t={t} />

      <h2 id="developments">{rt.developmentsTitle}</h2>
      <h3>{rt.newProject}</h3>
      <form action={createDevelopmentAction} className="card form">
        <input type="hidden" name="locale" value={locale} />
        <div className="field">
          <label htmlFor="dev-name">{rt.name}</label>
          <input id="dev-name" name="name" maxLength={200} required />
        </div>
        <div className="field">
          <label htmlFor="dev-kind">{rt.kind}</label>
          <select id="dev-kind" name="kind" required defaultValue="">
            <option value="">{t.form.choose}</option>
            {DEVELOPMENT_KINDS.map((k) => (
              <option key={k} value={k}>
                {(t.nearby.kinds as Record<string, string>)[k]}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="dev-status">{rt.status}</label>
          <select id="dev-status" name="status" required defaultValue="">
            <option value="">{t.form.choose}</option>
            {DEVELOPMENT_STATUSES.map((st) => (
              <option key={st} value={st}>
                {(t.nearby.statuses as Record<string, string>)[st]}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="dev-authority">{rt.authority}</label>
          <input id="dev-authority" name="authority" maxLength={200} required />
        </div>
        <div className="field">
          <label htmlFor="dev-url">{rt.sourceUrl}</label>
          <input id="dev-url" name="sourceUrl" type="url" inputMode="url" maxLength={500} placeholder="https://" />
        </div>
        <div className="field">
          <label htmlFor="dev-date">{rt.sourceDate}</label>
          <input id="dev-date" name="sourceDate" type="date" required />
        </div>
        <button type="submit">{rt.create}</button>
      </form>

      {projects.ok && projects.data.length > 0 && (
        <>
          <h3>{rt.linkTitle}</h3>
          <form action={linkDevelopmentAction} className="card form">
            <input type="hidden" name="locale" value={locale} />
            <div className="field">
              <label htmlFor="link-parcel">{rt.parcelId}</label>
              <input id="link-parcel" name="parcelId" required autoComplete="off" />
            </div>
            <div className="field">
              <label htmlFor="link-project">{rt.project}</label>
              <select id="link-project" name="projectId" required defaultValue="">
                <option value="">{t.form.choose}</option>
                {projects.data.map((pr) => (
                  <option key={pr.id} value={pr.id}>
                    {pr.name} · {(t.nearby.statuses as Record<string, string>)[pr.status] ?? pr.status} · {format(rt.linkedCount, { count: pr.linkedParcels })}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="link-distance">{rt.distanceKm}</label>
              <input id="link-distance" name="distanceKm" inputMode="decimal" />
            </div>
            <label className="choice">
              <input type="checkbox" name="mayAffectParcel" /> {rt.mayAffect}
            </label>
            <button type="submit">{rt.link}</button>
          </form>
        </>
      )}

      <h2>{x.subscriptionTitle}</h2>
      <ActivateSubscriptionForm locale={locale} t={t} />
    </section>
  );
}
