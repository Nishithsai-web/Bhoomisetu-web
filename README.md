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

## What works today (v0.1, Phase 0/1 foundation)

| Area | What is built | Status |
| --- | --- | --- |
| Area units | Gajam (sq yd), gunta, acre, cent, sq ft, sq m, hectare, ankanam; exact conversions. The web forms accept Telugu and Devanagari digits; the API's query parameters take ASCII digits | Built and tested |
| Land records | Parcels with survey numbers as printed (Telugu and Hindi letters kept), LGD village code, land class, area source, frontage, depth, facing | Built |
| Road access | Road width with its source (approved layout, measured on map, lister), road type, access right, distance to main road | Built |
| Duplicate land | Same land typed differently ("123/A/1", "0123-a-1"), with or without the village code, is detected and the second lister is pointed to the existing parcel. Spelling variants of village names still need LGD codes | Built |
| Sign-in | Indian mobile number + one-time code (stored only as a keyed hash); limits per number (cooldown, hour), per IP (IPv6 per /64) and a platform SMS budget; 24-hour session in an httpOnly cookie | Built (development SMS sender only; CAPTCHA and sign-out token revocation not yet) |
| Agent comparison | Effective price, low-price outlier flag, owner authority, checks, four sort orders, badges, score breakdown. A bare "I am the owner" claim is priced like an agent until a reviewer confirms it | Built and tested |
| Owner authority | Trust reviewer records the owner of record; the owner confirms agents from their offers or revokes them; all changes audited; reviewer rights re-checked on every action | Built |
| Web app | Home, search by village, mandal or district, area converter, agent comparison, phone sign-in, "List your land" and "Add your price" forms with plain-language hints, in `/en`, `/te`, `/hi` | Built |
| Road-widening layer | PostGIS tables and a "parcels near a road project" view | Schema only |
| Real SMS provider, identity checks, documents, maps, valuation | | Not started |

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
| `POST /api/v1/auth/otp/request` | Send a one-time code to a mobile number | No |
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
| `GET /api/v1/me/offers` | My listings: all my offers with how long since availability was confirmed | Yes |
| `POST /api/v1/parcels/{id}/offers/mine/still-available` | Confirm my land is still available | Yes |
| `POST /api/v1/parcels/{id}/offers/mine/sold` | Mark my offer as sold | Yes |
| `POST /api/v1/parcels/{id}/change-requests` | Ask to correct a land detail, with a reason | Listed, offered or owns the land |
| `GET /api/v1/me/change-requests` | My correction requests and their decisions | Yes |
| `GET /api/v1/admin/change-requests?status=pending` | Correction queue, oldest first | Trust reviewer |
| `POST /api/v1/admin/change-requests/{id}/decision` | Approve or reject (not your own, not land you are involved with) | Trust reviewer |
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

## Tests

```bash
dotnet test                                   # unit + architecture; set BHOOMISETU_TEST_DB to include API tests
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
