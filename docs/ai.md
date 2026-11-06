# AI in Bhoomisetu: plan and guardrails

Status: **planned, not built.** Nothing in the current code calls an AI model. This page records how AI will
be added so that it stays useful, safe and replaceable.

## Principles

1. **AI helps people; rules and people decide.** AI never states ownership, legal title, approval status,
   availability, transaction history or price unless that fact comes from Bhoomisetu's own records, with the
   source shown. Verification decisions are always made by a trust reviewer.
2. **Use the simplest tool that works.** Unit conversion, price per gajam, duplicate keys and ranking are
   plain code and stay that way: exact, cheap and testable.
3. **Every model is replaceable.** All model calls go through one gateway (planned `Modules.Assist`) that
   records the model, prompt version, latency, cost and result, so a model can be swapped by configuration.
4. **Measured before trusted.** Each task has a test set in Telugu, Hindi and English and a pass mark. A
   model or prompt change that drops below the mark is not released.

## Planned uses, in order

| Use | What AI does | What stays with rules or people | First pass mark |
| --- | --- | --- | --- |
| Search in your own words ("2 acres near Shankarpalle under 1 crore", in Telugu) | Turns the sentence into filters | The search itself runs on the database | 90% of filters exactly right |
| Help writing a listing | Suggests a clear description from the form fields | The lister reads and approves it | No fact that is not in the form |
| Translate listing descriptions | Telugu ↔ Hindi ↔ English, labelled "auto-translated" | Interface text is always human-translated | Native-speaker score 4/5 |
| Read land documents (pahani, 1-B, sale deed) | Pulls out survey number, area, owner name | A trust reviewer confirms every field | 95% per field, low-confidence fields flagged |
| Spot duplicate or suspicious listings | Flags likely duplicates from photos and text | A person decides to merge or remove | 95% precision |
| Voice search for people who prefer speaking | Speech to text in Telugu and Hindi | Same as search | Word error rate measured per region |

Candidate models and services, and how they will be compared, are in the technology decision register
(TDR-10 to TDR-13) linked from the README.

## Privacy

Documents and personal details are sent only to providers whose terms forbid training on our data, with
processing in India preferred. Prompts never include phone numbers.
