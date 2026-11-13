# Bhoomisetu (భూమిసేతు · भूमिसेतु)

**A trusted way to find, compare and verify land and homes in India, in Telugu, Hindi and English.**

Bhoomisetu means "bridge to land". In India the same plot is often offered by several agents at different
prices, area is quoted in gajams, guntas or acres depending on who you ask, and buyers cannot tell which
listing is genuine. Bhoomisetu fixes that with three ideas:

1. **One record per piece of land.** A plot is identified by its survey number and village, so ten agents
   listing the same land show up as ten offers on one parcel, not ten separate listings.
2. **Every offer compared side by side.** Price to the buyer (including brokerage), price per gajam, whether
   the owner has authorised the agent, what checks the lister passed, how fast they reply. Ranked by a
   published "best value" rule, not just the lowest price, and never by paid promotion.
3. **Say exactly what was checked.** Badges name the check done (phone code, identity, owner confirmation).
   Nothing more is claimed: a check is not a legal title opinion.

The first release targets **Telangana: plots, farmland and verified resale or rental homes**. The data model
is national, with state rules added as "jurisdiction packs".

## What works today

Status is one of **Built** (in the code, covered by tests, passing CI), **Partial** (works, with a gap named in the
row) or **Not started**. The live, editable list of what is done, next and remaining is kept in the project's
build tracker; the reasons behind each choice are in [the decision log](docs/decisions/README.md).

| Area | What is built | Status |
| --- | --- | --- |
| Area units | Gajam (sq yd), gunta, acre, cent, sq ft, sq m, hectare, ankanam; exact conversions. The web forms accept Telugu and Devanagari digits | Built |
| Land records | One record per piece of land: survey number as printed (Telugu and Hindi letters kept), LGD village code, land class, area source, frontage, depth, facing | Built |
| Road access | Road width with its source, road type, access right, distance to the main road (shown in feet) | Built |
| Duplicate land | The same land typed differently ("123/A/1", "0123-a-1") is detected and the second lister is pointed to the existing record | Built |
| Search in three scripts | Village, mandal or district typed in Telugu, Hindi or English letters finds the same land through a sound-alike key (Kurnool and కర్నూలు included) | Built |
| Sign-in | Indian mobile number and one-time code (keyed hash only), limits per number, per requester and per IP, human check from the second code (Turnstile), server-side sign-out | Partial: development SMS sender only |
| Agent comparison | Effective price with brokerage, published best-value score, low-price outlier flag, owner authority, four sort orders | Built |
| Owner authority | A reviewer records the owner of record; the owner confirms or revokes agents; every change audited | Partial: confirm/revoke has API endpoints, no screen yet |
| Compare with land nearby | Land for sale within 10 km priced per sq yd and per gunta, with rule-based pros and cons | Built |
| Documents | Uploads limited to a fixed list (brochure, site plan, maps, HMDA/DTCP, RERA, building permission, photos) chosen from a dropdown; sale deeds never uploaded; shown to buyers only after a reviewer's check | Built |
| Plans and contact | Free visitors see land facts, survey number and prices; signed-in buyers get the lister's WhatsApp number; owners and agents pay a monthly plan, recorded by a reviewer from the UPI reference | Partial: no online checkout |
| Maps | Pin on every land, live map explorer: place search in three scripts (our places first, then OpenStreetMap), price tags, "New this week", grouped pins, list beside the map, shareable views | Partial: OpenStreetMap tiles and place search must be replaced by licensed services before launch |
| Homes for rent | Listing, map, rent per sq ft compared with homes within 5 km, homes near any land; public location rounded to about 110 m | Built |
| Reviewer screens | Queues for documents, corrections, duplicates, reports and owner records | Built |
| Web app | Every screen in `/en`, `/te` and `/hi`, following the Bhoomisetu design | Built |
| Road-widening layer | PostGIS tables and a "parcels near a road project" view | Partial: no screen |
| Real SMS, online payments, identity checks for listers, valuation, production hosting | | Not started |

## How it is built

```
apps/web                      Next.js 16 web app (te / hi / en)
src/Bhoomisetu.Api            ASP.NET Core (.NET 10) API host: endpoints, auth, rate limits, persistence
src/Bhoomisetu.Modules.Land   Area units, survey numbers, parcel identity
src/Bhoomisetu.Modules.Offers Multi-agent comparison and best-value ranking
src/Bhoomisetu.Modules.Identity  Indian mobile numbers, OTP
src/Bhoomisetu.SharedKernel   Result type, clock, digit conversion
db/migrations                 SQL schema (PostgreSQL 16+, PostGIS); the single source of truth
db/tests                      SQL constraint tests
tests/                        Unit, architecture-boundary and API integration tests
shared/fixtures               Test data shared by the .NET and web tests
docs/decisions                Why the code is the way it is
```

One deployable application with strict module boundaries (a test fails if modules depend on each other).
Why each technology was chosen, and what was rejected, is in [docs/decisions](docs/decisions/README.md).

## API at a glance

| Method and path | Purpose | Sign-in |
| --- | --- | --- |
| `POST /api/v1/auth/otp/request` | Send a one-time code to a mobile number (from the second code in an hour, a human check when `Captcha:Provider` is `turnstile`) | No |
| `POST /api/v1/auth/otp/verify` | Exchange the code for a session token | No |
| `GET /api/v1/me` | Current user and checks passed | Yes |
| `GET /api/v1/units/area?value=200&unit=square_yard` | Area in every unit | No |
| `POST /api/v1/parcels` | Create a parcel (409 with the existing id if the land is already listed) | Yes |
| `GET /api/v1/parcels?q=mokila` | Search parcels by village, mandal or district (`village`, `mandal`, `district`, `state` also work) | No |
| `GET /api/v1/parcels/{id}` | Parcel details, area in all units, road facts | No |
| `POST /api/v1/parcels/{id}/offers` | Add or replace your offer | Yes |
| `POST /api/v1/parcels/{id}/offers/mine/withdraw` | Withdraw your offer | Yes |
| `GET /api/v1/parcels/{id}/comparison?sort=best_value` | Compare all offers | No |
| `POST /api/v1/parcels/{id}/offers/{offerId}/confirm-lister` | Owner confirms the agent who made an offer | Owner of record |
| `POST`/`DELETE /api/v1/parcels/{id}/agents/{userId}/confirm` | Owner confirms or revokes an agent by account | Owner of record |
| `POST /api/v1/admin/parcels/{id}/owner-of-record` | Record the owner after document review | Trust reviewer |
| `POST /api/v1/auth/sign-out` | End all of my sessions on the server | Yes |
| `GET /api/v1/me/offers` | My listings: all my offers with how long since availability was confirmed | Yes |
| `GET /api/v1/me/parcels-without-price` | Land I listed but have not priced yet | Yes |
| `POST /api/v1/parcels/{id}/offers/mine/still-available` | Confirm my land is still available | Yes |
| `POST /api/v1/parcels/{id}/offers/mine/sold` | Mark my offer as sold | Yes |
| `POST /api/v1/parcels/{id}/change-requests` | Ask to correct a land detail, with a reason | Listed, offered or owns the land |
| `GET /api/v1/me/change-requests` | My correction requests and their decisions | Yes |
| `GET /api/v1/admin/change-requests?status=pending` | Correction queue, oldest first | Trust reviewer |
| `POST /api/v1/admin/change-requests/{id}/decision` | Approve or reject (not your own, not land you are involved with) | Trust reviewer |
| `POST /api/v1/admin/duplicates/{parcelId}/merge` | Merge a record set aside as a duplicate into the kept one | Trust reviewer |
| `GET /api/v1/parcels/{id}/viewer` | Am I the owner of record, which offer is mine, which agents are confirmed | Yes |
| `DELETE /api/v1/parcels/{id}/offers/{offerId}/confirm-lister` | Owner removes a confirmation made from an offer | Owner of record |
| `GET /api/v1/admin/duplicates` | Records set aside as possible duplicates, with the kept record | Trust reviewer |
| `GET /api/v1/admin/users/by-phone?phone=` | Find an account by mobile number (audited, 30 per reviewer per hour) | Trust reviewer |
| `GET /api/v1/document-types` | Allowed document types (brochure, site plan, maps, HMDA/DTCP approval, RERA, building permission, photos) and the never-upload list | No |
| `GET /api/v1/parcels/{id}/documents` | Checked-document counts for everyone; documents for subscribed buyers; your own uploads | No |
| `POST /api/v1/parcels/{id}/documents` | Add a public document (multipart: `type`, `file`, `referenceNumber`, `declaration=true`) | Owner/agent with a subscription |
| `GET /api/v1/parcels/{id}/documents/{docId}/file` | Open a document (checked ones for subscribed buyers; uploader and reviewers always) | Yes |
| `DELETE /api/v1/parcels/{id}/documents/{docId}` | Remove your document | Uploader |
| `GET /api/v1/parcels/{id}/offers/{offerId}/contact` | Lister's WhatsApp number (lister must be subscribed; 50 per day per person, audited) | Yes |
| `GET /api/v1/me/subscription`, `PUT /api/v1/me/whatsapp` | My subscription status; set my WhatsApp number | Yes |
| `GET /api/v1/admin/documents`, `POST /api/v1/admin/documents/{id}/decision` | Document check queue; approve or reject with a reason | Trust reviewer |
| `POST /api/v1/admin/subscriptions` | Record a subscription payment (UPI or receipt reference) | Trust reviewer |
| `GET /api/v1/parcels/map?south&west&north&east&landClass&newDays` | Pinned land in the visible map area with lowest price and listing date (max 500; `newDays=7` for new listings only) | No |
| `GET /api/v1/places?q=&lang=` | Places matching a name in any script: our villages, mandals and districts with listing counts, then OpenStreetMap places | No |
| `GET /api/v1/parcels/{id}/nearby` | This land and land for sale within 10 km: price per sq yd and per gunta, road, checks, factual pros and cons | No |
| `GET /api/v1/parcels/{id}/nearby-rentals` | Homes for rent within 5 km of the land | No |
| `POST /api/v1/rentals`, `GET /api/v1/rentals/{id}` | List a home for rent (with map point); read one | Yes / No |
| `GET /api/v1/rentals/map`, `GET /api/v1/rentals/{id}/nearby` | Homes for rent in the map area; compare with homes within 5 km (rent per sq ft, pros and cons) | No |
| `POST /api/v1/rentals/{id}/status`, `GET /api/v1/me/rentals`, `GET /api/v1/rentals/{id}/contact` | Still available / rented / remove / relist; my homes; WhatsApp of a subscribed lister | Yes |
| `PUT /api/v1/parcels/{id}/pin` | Set the map pin (phone GPS or typed coordinates, inside India); shown on the land page with directions | Owner, agent with a price, or reviewer |
| `GET /api/v1/parcels/{id}/developments` | Nearby developments and road widening, with distance, status, official source and date | No |
| `POST /api/v1/parcels/{id}/reports` | Report a listing (wrong details, sold, fraud, duplicate, dispute, other) | Yes |
| `GET/POST /api/v1/admin/developments`, `PUT /api/v1/admin/developments/{id}` | List, add and update developments (https source required unless "proposed, unverified") | Trust reviewer |
| `PUT/DELETE /api/v1/admin/parcels/{id}/developments` | Link a development to land with distance and "may affect this land" | Trust reviewer |
| `GET /api/v1/admin/reports`, `POST /api/v1/admin/reports/{id}/decision` | Report queue; resolve or dismiss with a note | Trust reviewer |
| `GET /health/live`, `/health/ready` | Health checks | No |

Errors use RFC 9457 problem responses with a stable `code` (for example `parcel.already_exists`) that the
web app translates.

## Run it locally

Needs the .NET 10 SDK, Node.js 22.18+ and Docker.

```bash
docker compose up -d db

export ConnectionStrings__Bhoomisetu="Host=localhost;Database=bhoomisetu;Username=bhoomisetu;Password=bhoomisetu"
export Auth__SigningKey="$(openssl rand -base64 48)"
export Otp__Pepper="$(openssl rand -base64 32)"
dotnet run --project src/Bhoomisetu.Api        # http://localhost:5080, applies migrations in Development

cd apps/web
cp .env.example .env.local
npm install
npm run dev                                    # http://localhost:3000
```

In Development (and the automated "Testing" environment) the one-time code is written to the API log
instead of being sent by SMS. The API refuses to start in any other environment until a DLT-registered SMS
provider is added and `ForwardedHeaders:KnownProxies` or `KnownNetworks` lists the load balancer and the web
server, so rate limits see real visitor IPs.

If your local database applied an earlier version of migration 0003 (before 2026-11-07), the API stops with
"Migration 0003 was changed after it was applied". Reset the local database once with
`docker compose down -v && docker compose up -d db`.

In production the web server must sit behind a load balancer that appends the visitor's IP to
`X-Forwarded-For`; set `BHOOMISETU_BEHIND_LOAD_BALANCER=true` on the web server and list the web server in the
API's `ForwardedHeaders` settings. Without both, per-visitor rate limits do not work.

## Tests

```bash
dotnet test --solution Bhoomisetu.slnx        # unit + architecture; set BHOOMISETU_TEST_DB to include API tests
cd apps/web && npm test && npm run check:i18n && npm run typecheck
psql -v ON_ERROR_STOP=1 -f db/tests/core_constraints.sql   # after applying db/migrations
```

CI (`.github/workflows/ci.yml`) runs all of these against PostgreSQL with PostGIS on pushes to `main` and on
pull requests.

## Guides

- [How Bhoomisetu is built](docs/ARCHITECTURE.md): from the plain idea to the engineering rules
- [Recipes for changing it safely](docs/CONTRIBUTING.md): add a field, a language, an endpoint; change ranking
- [AI plan and guardrails](docs/ai.md)
- Settings you can change without code (ranking weights, OTP limits, rate limits): `src/Bhoomisetu.Api/appsettings.json`

## Product documents

- [Product requirements](https://claude.ai/artifact/VpHFWLqkgcwAdGSFYCKWna)
- [CTO findings, execution plan and technology decision register](https://claude.ai/artifact/1QVmUyEx3vxjLb381WUq4L)

## Important limits

- Land-record portals such as Bhu Bharati are not automated: that needs the authority's permission or an
  official API. Until then owners upload extracts and buyers get a link to check the official portal.
- A verified phone code proves someone controls a number, not that they own the land.
- Telugu and Hindi text must be reviewed by native speakers before public release.
