# Technology decision register

The full register, with alternatives, evidence, costs and validation plans, is in the
[CTO findings and decision register](https://claude.ai/artifact/1QVmUyEx3vxjLb381WUq4L). This file records what
the code in this repository implements, so a reader of the code can see why it looks the way it does.
Change a decision by adding a dated entry here and updating the register; never silently.

| ID | Decision | Status in code |
| --- | --- | --- |
| TDR-01 | Modular monolith: one deployable, modules depend only on the shared kernel | Implemented; enforced by `tests/Bhoomisetu.ArchitectureTests` |
| TDR-02 | ASP.NET Core on .NET 10 LTS (supported to 14 Nov 2028) | Implemented (`src/Bhoomisetu.Api`) |
| TDR-03 | Next.js 16, server-rendered pages with `/en`, `/te`, `/hi` in every URL | Implemented (`apps/web`) |
| TDR-04 | Responsive web first, React Native later | Web only so far |
| TDR-05 | PostgreSQL + PostGIS + pg_trgm; pgvector deferred until semantic search (Phase 3) | Implemented in `db/migrations` |
| TDR-06 | Search in PostgreSQL first (ILIKE + trigram index); OpenSearch only when triggers are hit | Implemented: `GET /api/v1/parcels` |
| TDR-07 | AWS Mumbai primary, Hyderabad for backups | Not started (no infrastructure code yet) |
| TDR-14 | SMS OTP through two DLT-registered providers | Interface only (`IOtpSender`); a development sender logs codes and the API refuses to start outside Development with it |
| TDR-15 | Own phone-OTP sign-in issuing 24-hour JWTs, kept in an httpOnly cookie by the web app | Implemented; reviewer rights re-checked in the database on every admin call; refresh and revocation not yet |
| TDR-16 | Background jobs | Not started |
| TDR-17 | OpenTelemetry | Not started |
| TDR-18 | GitHub Actions; OpenTofu for infrastructure | CI implemented; OpenTofu not started |

## Decisions made while coding (2026-11-06)

1. **SQL migrations are the schema's source of truth, not EF Core migrations.** Plain SQL is reviewable by
   anyone, testable with `psql` (see `db/tests`), and the same for every tool. EF Core only maps onto it; an
   integration test fails if a mapped column is missing.
2. **One parcel per survey number and village.** The parcel key uses the LGD village code when known and the
   normalised names otherwise. Names in different scripts will not match until LGD codes are captured for
   every listing; that is the next data task.
3. **Best value is a published weighted score, not the lowest price.** Weights: price 40%, owner authority
   25%, checks 15%, response time 10%, freshness 10%. A price below 70% of the median of the other offers is
   flagged "verify before visiting" and ranked last. Undisclosed brokerage is assumed at 2% so hiding fees
   never looks cheaper. These numbers are product decisions to be reviewed with real data.
4. **Owner of record is set only by a trust reviewer**, who cannot set themselves, and every change is in the
   append-only audit log. Agent confirmations made by a previous owner stop counting when ownership changes.
5. **Bigha is not a national unit.** Its size differs by state, so it will live in state jurisdiction packs.

## Changes after the first QA review (2026-11-06)

6. **An "owner" claim is not trusted until a reviewer records the owner of record.** Until then the offer is
   priced with assumed brokerage, scored like an agent, gets no "no brokerage" badge, and shows "says owner
   (not yet verified)".
7. **A low-price outlier never gets the "lowest price" badge**, so the bait warning and a reward never
   appear together.
8. **OTP limits are atomic per number** (advisory lock), hourly per number, per IP (IPv6 per /64), with a
   platform-wide SMS budget that is logged when hit. The daily cap tried first was removed after QA showed it
   let anyone lock an owner out for a day. Residual risk: someone can still use up a number's hourly quota;
   a CAPTCHA after the first request is the planned fix before public launch.
9. **Every parcel keeps a name-based key** as well as the LGD key, so leaving out the village code cannot
   create a duplicate (migration 0003).
10. **Survey numbers keep Telugu and Devanagari letters as printed**; no transliteration, because it is
    ambiguous.
11. **Ranking weights and OTP limits are configuration**, validated at start-up.

## Changes after the second QA review (2026-11-06)

12. **Migration 0003 was corrected in place, once.** It failed on databases that already held the same land
    twice. No shared database had applied it, so the file was fixed instead of adding a new one; later
    duplicates are set aside in `parcel_duplicate_candidates` for a reviewer to merge. From now on applied
    migrations are never edited.
13. **The web server forwards the visitor's IP** (last X-Forwarded-For entry) and the API trusts it only from
    configured proxies or networks, so per-IP limits apply per visitor.
14. **A reviewer cannot decide corrections for land they are involved with**, and decisions lock the request
    row so two reviewers cannot both apply it. A corrected road width is labelled "stated by lister".
15. **Known gaps, accepted for now:** anyone can make an offer on any parcel and so become able to request
    corrections (reviewers see who asked); real owners pay the assumed 2% brokerage in comparisons until a
    reviewer records them, and the document-upload and review screens are not built yet; owners can confirm
    agents through the API but not yet from a web screen.

## Changes after the third QA review (2026-11-07)

16. **CI is the compiler of record.** The authoring workspace has no .NET SDK; GitHub Actions builds with
    warnings as errors and runs all tests (155 at the time of writing) against PostGIS, plus a production
    smoke test of the web server. Tests run on Microsoft.Testing.Platform (`global.json`).
17. **OTP quotas are counted per requester** (IP or IPv6 /64) with a higher per-number cap (10/hour) and 3
    tries per code, so a stranger cannot lock out an owner and guessing stays bounded (about 30 guesses per
    number per hour). A CAPTCHA remains the planned fix for distributed attacks.
18. **Sign-out ends sessions on the server**: tokens carry a version that sign-out bumps (migration 0005).
19. **Set-aside duplicates are hidden** from search and comparison until a reviewer merges them.
20. **The web server forwards visitor IPs only when declared to sit behind a load balancer**
    (`BHOOMISETU_BEHIND_LOAD_BALANCER=true`); otherwise the header could be forged.
21. **Human check (Cloudflare Turnstile) from the second code per number per hour.** Chosen over hCaptcha and
    reCAPTCHA for no image puzzles on low-end phones and no ad tracking. A request that passes the check skips
    the normal per-number limit (see 24), so strangers cannot lock an owner out; the per-requester limit and SMS budget still
    apply. Production refuses to start with `Captcha:Provider=none` unless `AllowDisabledInProduction` is set.
22. **Merging a duplicate moves everything a buyer relies on**: the owner of record and agent confirmations move
    across, each lister keeps their most recent price, different owners block the merge for document review,
    and a reviewer involved with either record cannot merge. Writes to a hidden duplicate get 409 with the kept id.
23. **Reviewers may look up at most 30 accounts by phone per hour**; more is treated as misuse and logged.
24. **Human-checked code requests keep a hard cap of 20 per number per hour** (normal cap 10), so an owner can
    always get past strangers using up the normal cap, while solved checks still cannot flood one phone.
25. **Free, subscribed and paid levels.** Free visitors see land facts, the survey number and price comparison;
    signing in (free) opens WhatsApp contact. Owners and agents pay a lister subscription, which turns on WhatsApp
    contact and document uploads. Buyers open checked documents with a viewer subscription whose price is a setting
    (`Subscriptions:ViewerPriceRupees`, default 0 = free sign-up). Payments are taken outside the app (UPI) and
    recorded by a reviewer with the payment reference; a payment gateway (Razorpay or Cashfree) is proposed.
26. **Only public papers are uploaded**, chosen from a fixed dropdown: brochure, site plan, location map,
    HMDA/DTCP layout approval, RERA certificate, building permission, photos. Sale deed, link documents, EC,
    pattadar passbook, pahani/ROR-1B and ID cards are never accepted; the uploader confirms this with a tick box.
    The file type is read from the file's own bytes; PDF, JPEG, PNG and WebP up to 10 MB.
27. **Documents appear to buyers only after a trust reviewer checks them.** Rejected files are deleted and the
    reason kept. The site tells buyers to see original ownership papers in person and pay token money only after that.
28. **Files live outside the database** behind `IDocumentStorage` (local folder now; S3-compatible storage and a
    virus scan such as ClamAV are proposed before launch). Storage keys are generated, never taken from the uploader.
