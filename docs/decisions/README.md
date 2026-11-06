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
| TDR-15 | Own phone-OTP sign-in issuing short-lived JWTs | Implemented; refresh tokens not yet |
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
