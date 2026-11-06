# How Bhoomisetu is built

This guide is written in three layers. Read only as far as you need.

- **Level 1, the idea** (no coding knowledge needed)
- **Level 2, finding your way in the code** (new developers)
- **Level 3, the rules that keep it healthy** (experienced engineers and architects)

---

## Level 1: the idea in plain words

Think of Bhoomisetu as three parts that talk to each other:

```
 People (phone or laptop)
        │  web pages in Telugu, Hindi or English
        ▼
 Web app  (apps/web)        shows pages, forms and results
        │  asks questions like "compare offers for this land"
        ▼
 API      (src/)            checks the rules and does the work
        │  reads and saves records
        ▼
 Database (db/)             keeps land, offers, people and the audit log
```

- The **web app** is what people see. It never decides anything important; it only shows and collects.
- The **API** holds the rules: what a valid survey number is, who may say they own land, how offers are
  ranked. Every rule lives here once, so the web app, a future mobile app and partners all get the same answer.
- The **database** stores everything and also double-checks the most important rules (for example, that
  the same land cannot be listed twice), so a bug in code cannot quietly break them.

Three product ideas run through everything:

1. **One record per piece of land**, identified by survey number and village. Ten agents selling the same
   plot become ten offers on one record.
2. **Compare offers fairly.** The "best value" rule is published and the same for everyone; nobody can pay
   to move up.
3. **Say exactly what was checked.** A badge says "phone code checked", never a vague "verified".

---

## Level 2: finding your way in the code

### Where things are

| You want to change… | Look in |
| --- | --- |
| Words on a page, in any language | `apps/web/messages/en.json`, `te.json`, `hi.json` |
| How a page looks | `apps/web/app/globals.css` |
| What a page shows | `apps/web/app/[locale]/…/page.tsx` |
| A form | `apps/web/components/*Form.tsx`, its checks in `apps/web/lib/forms.ts` |
| A business rule (units, survey numbers, ranking, OTP) | `src/Bhoomisetu.Modules.*` |
| An API address (endpoint) | `src/Bhoomisetu.Api/Endpoints/*.cs` |
| Settings such as ranking weights or OTP limits | `src/Bhoomisetu.Api/appsettings.json` |
| The database tables | a **new** file in `db/migrations/` (never edit an old one) |

### What happens when someone lists land

1. The person fills the form on `/te/sell` (or `/en`, `/hi`).
2. `apps/web/lib/forms.ts` checks the form and converts feet to metres; problems are shown next to the field
   in plain words, in their language.
3. A server action (`apps/web/app/[locale]/actions.ts`) sends it to `POST /api/v1/parcels` with the person's
   session. The session is a cookie the page scripts cannot read.
4. `ParcelEndpoints.cs` checks it again with the domain rules (`ParcelLocation`, `SurveyNumber`, `Area`).
5. If the same land already exists (same survey number and village, with or without village code), the API
   answers 409 with the existing parcel, and the person is sent to add their price to it.
6. Otherwise the parcel and an audit entry are saved in one transaction.

### Running and testing

See the README. In short: `docker compose up -d db`, `dotnet run --project src/Bhoomisetu.Api`,
`cd apps/web && npm run dev`. Tests: `dotnet test`, `npm test`, `npm run check:i18n`.

---

## Level 3: rules that keep it healthy

### Shape: a modular monolith

One deployable API, split into modules with strict boundaries (decision TDR-01):

- `SharedKernel`: tiny shared types (`Result`, `IClock`, digit conversion). No business rules.
- `Modules.Land`, `Modules.Offers`, `Modules.Identity`: pure domain rules. **No database, no HTTP, no other
  module.** This keeps them fast to test and easy to move into a separate service if one ever needs to scale.
- `Api`: the only place that knows about HTTP, EF Core, PostgreSQL and configuration.

`tests/Bhoomisetu.ArchitectureTests` fails the build if a module references another module, a package, or
reads the system clock directly.

### Rules worth knowing before you change anything

| Rule | Why | Enforced by |
| --- | --- | --- |
| The SQL migrations are the schema; EF Core only maps onto them | Reviewable by anyone, same for every tool | `MigrationRunner` checksums; mapping test |
| Applied migrations are never edited | Databases already ran them | checksum mismatch stops start-up |
| Every rule exists in the API; the database repeats the critical ones | A code bug must not create duplicate land or a second owner | CHECK constraints, unique indexes, `db/tests` |
| Errors carry a stable `code` | The web app translates codes; English text is only for logs | `ApiResults`, `apps/web/lib/errors.ts` |
| Time comes from `IClock` | Rules about expiry and freshness must be testable | architecture test |
| Anything that changes trust (owner, agent confirmation) is audited in the same transaction | Disputes need evidence | `AuditWriter`, append-only trigger |
| Ranking weights are configuration, published to users | Fairness must be visible and changeable without code | `Ranking` in `appsettings.json`, `RankingOptions.Validate` |
| Personal data stays minimal: phone numbers are hashed into OTPs and masked in responses | DPDP-aligned, less to leak | code review, tests |

### Security model in one page

- Sign-in is phone + one-time code. Codes are stored as HMAC hashes with a server secret, compared in
  constant time, limited per number (cooldown, hourly, daily), per IP, and by a platform-wide SMS budget.
  Requests for one number are serialised with a database advisory lock.
- The API issues a signed token (24 hours). The web app keeps it in an httpOnly cookie.
- Roles: anyone signed in can list land and offers; only the **owner of record** can confirm agents; only a
  **trust reviewer** can record an owner, and the database is re-checked on every reviewer action so a removed
  reviewer loses access at once.
- The API refuses to start outside Development until a DLT-registered SMS provider is configured, and outside
  local environments until trusted proxy addresses are set (so rate limits see real client IPs).

### Where AI fits (planned)

See [ai.md](ai.md). AI never decides ownership, title, approval or price; it helps people search, write and
read documents, always with a human or a rule making the final call.

### How to extend safely

Step-by-step recipes: [CONTRIBUTING.md](CONTRIBUTING.md).
