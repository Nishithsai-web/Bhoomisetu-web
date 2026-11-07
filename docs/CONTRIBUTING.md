# Recipes: changing Bhoomisetu safely

Each recipe lists the files to touch, in order, and the check that proves it worked. If a recipe does not
cover your change, add one here when you are done.

## Change a word or sentence on the website

1. Edit the text in `apps/web/messages/en.json`, then the same key in `te.json` and `hi.json`.
2. Keep `{placeholders}` exactly as they are.
3. Run `cd apps/web && npm run check:i18n`. It fails if a language misses a key, leaves it empty, or drops a
   placeholder.
4. Ask a native Telugu and Hindi speaker to review new text before release. Use the words villagers use
   (gajam, gunta, pahani), short sentences, and say what to do, not what went wrong technically.

## Add a new language (for example Kannada)

1. Copy `apps/web/messages/en.json` to `kn.json` and translate it.
2. Add `"kn"` to `LOCALES`, `INTL_LOCALE` (`kn-IN`) and `LANGUAGE_NAMES` in `apps/web/lib/i18n.ts`.
3. Add the font (for example `Noto_Sans_Kannada`) in `apps/web/app/[locale]/layout.tsx` and the
   `html:lang(kn)` rule in `globals.css`.
4. Add `'kn'` to the `preferred_locale` CHECK with a new migration.
5. Add `kn` to `scripts/check-translations.mjs` and to the language switcher regex in `LanguageSwitcher.tsx`.
6. Run `npm test && npm run check:i18n`.

## Change how offers are ranked

1. Change the numbers under `Ranking` in `src/Bhoomisetu.Api/appsettings.json` (or an environment variable,
   for example `Ranking__PriceWeight=0.45`). Weights must add up to 1; the API refuses to start otherwise.
2. Record what changed and why in `docs/decisions/README.md`. Ranking is published to users; changes are
   product decisions.
3. If the rule itself changes (not just a weight), edit `BestValueRanker.cs` and its tests in
   `tests/Bhoomisetu.UnitTests/Offers/`. Recalculate expected numbers independently (a spreadsheet or a small
   script), never by copying what the code prints.

## Add a field to a parcel (for example "borewell depth")

1. **Database:** add a migration `db/migrations/000N_borewell_depth.sql` with the column and a CHECK for its
   allowed range. Add a case to `db/tests/core_constraints.sql`.
2. **API:** add the property to `ParcelRecord` (`Persistence/Records.cs`), the request and response records
   and the validation in `Endpoints/ParcelEndpoints.cs`. Validate the same range as the CHECK.
3. **Web:** add the field to `parseParcelForm` in `apps/web/lib/forms.ts` with a test in `forms.test.ts`, add
   the input to `components/ParcelForm.tsx`, and add the label and a hint in all three message files.
4. Run `dotnet test --solution Bhoomisetu.slnx`, `npm test`, `npm run check:i18n`. The integration test
   `Every_mapped_column_exists_in_the_database` catches a property without a column.

## Add an API endpoint

1. Put it in the matching file under `src/Bhoomisetu.Api/Endpoints/`, or a new `XyzEndpoints.cs` with a
   `MapXyzEndpoints` method called from `Program.cs`.
2. Validate input, return `ApiResults.Invalid/NotFound/Forbidden/Problem` with a stable `code`, and add that
   code's message to the `errors` section of every message file.
3. Decide who may call it: `.RequireAuthorization()` for signed-in users, the `TrustReviewer` policy for
   reviewers, and resource checks (for example "is this the owner of record?") inside the handler.
4. Audit anything that changes trust or money with `AuditWriter` before `SaveChangesAsync`.
5. Add an integration test in `tests/Bhoomisetu.IntegrationTests/ApiFlowTests.cs`, including a permission test.
6. Add it to the API table in the README.

## Support a new state (jurisdiction pack)

Units, approval bodies and land-record names differ by state. Keep national code free of state rules:

1. State-specific units (for example bigha, whose size varies) go in a state pack, not in `AreaUnit`.
2. Approval types, record names (pahani, 7/12, khatauni) and authority links go in configuration or data
   tables per state, with source and date.
3. Add the state's words to the message files and a hint that names the state's own documents.

## Before you open a pull request

- `dotnet build` with no warnings (warnings fail the build), `dotnet test --solution Bhoomisetu.slnx`, `npm test`, `npm run check:i18n`,
  `npm run typecheck`.
- New behaviour has a test; a fixed bug has a test that failed before the fix.
- No secrets, phone numbers or document contents in code, logs or test data.
