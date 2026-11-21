# Project Status

Updated: 2026-11-21 · Last logged commit: 056ac3a

`schema.graphql` is the API contract and code is the authority; this file is the index and the rules neither shows at a glance.

## Milestones
| # | Milestone | Status |
|---|---|---|
| M0 | Scaffold, CI, Scalar, EF Core + SQLite, Docker | ✅ done |
| M1 | Identity + cookie auth, Owner/Guest seed | ✅ done |
| M2 | Listings, tracking, contacts | 🟡 done except general notes |
| M3 | Profile (one master profile per user) | ✅ done; English CV import API done |
| M4 | Dashboard stats | ⬜ |
| M5 | LLM layer, usage logging, settings | 🟡 done except Ollama |
| M6 | Parse queue + SSE + cache | ⬜ |
| M7 | Guest sandbox reset | ⬜ personal-provider limits done; shared Gemini exempt |
| M8 | Deploy, README | ⬜ |

## REST
Everything requires auth unless marked anon.
- `GET /api/health` (anon)
- `POST /api/auth/login` (anon, rate-limited) → `{requiresTwoFactor, user}`; 401 bad credentials, 429 locked out
- `POST /api/auth/guest` (anon, rate-limited), `POST /api/auth/logout` (anon), `GET /api/auth/me`
- `GET /api/documents/{id}`: PDF inline, `nosniff`, 404 if not own
- `GET /api/base-cv-versions/{id}/pdf`: rendered on request, never stored
- Dev only (anon): `/openapi/v1.json`, `/scalar`
- `POST /graphql`: SDL in `schema.graphql`; `SchemaTests` rewrites the file and fails once when it's stale

## GraphQL rules by feature
- **Conventions**: all data scoped by the global user filter. Typed errors on mutations only; queries report bad input as top-level errors. `listings` uses offset paging, lookups use cursors. `search` matches normalized columns because SQLite only folds ASCII. A default order applies only without a client `order`.
- **Listings** (`Features/Listings`): company, role and skills come in by name, found or created. `updateListing` is a full replace, except `contacts` (null keeps links; changing company clears them) `trainingProgram` (omitted keeps, null removes) and `originalDescription` (omitted keeps; dropped when the posting is English or has no language). Title, company and role ≤200, description and original ≤20000. `description` is always English; `originalDescription` holds the same key points in the ad's language, and CV, cover letter and requirement map prompts send it when they write in that language (else the English one). `deleteListing` is blocked once status ≠ SAVED. Favorite, shortlist and archive keep the first timestamp and leave `updatedAt` alone. `setListingShortlisted` manages an independent review list via `shortlistedAt`; filtering/sorting uses the standard listing query. The `stageRank` sort orders by pipeline stage (OFFER > INTERVIEWING > APPLIED > SAVED > ACCEPTED > REJECTED/WITHDRAWN), e.g. for favorites, since `status` sorts alphabetically.
- **Listing URLs**: normalized (no `utm_*`, `fbclid`, `gclid`, fragment, trailing `/`), unique per user among open listings, archived included; closed listings are earlier attempts. `createListing` also returns `previousAttempts` and `possibleDuplicates` (same company and title, other URL; for non-vacancies also other active ones at the company). Partial index must match `ApplicationStatuses.Final`. [Preflight guide](listing-url-preflight.md).
- **Listing kinds**: VACANCY (default), OPEN_INVITATION, UNSOLICITED (no url, postedOn or deadline).
- **Due date** (`ListingQueries.WithDue`, drives order, `dueWithinDays`, `dueOn`): DATE → `deadlineOn`, ASAP → `postedOn`+14, UNSPECIFIED → `postedOn`+30, UNSOLICITED → null. Expired affects only order and filter.
- **Applications**: SAVED→APPLIED→INTERVIEWING (first `addInterview`)→OFFER/REJECTED/WITHDRAWN; APPLIED→OFFER/REJECTED/WITHDRAWN; OFFER→ACCEPTED/REJECTED/WITHDRAWN. Final = REJECTED, WITHDRAWN, ACCEPTED. Dates not in the future, nothing before the applied date. `applyToListing` is the only way to mark documents sent and re-dates their file names; undoing APPLIED unsends them. Documents are optional on apply. Applying clears `shortlistedAt`; undo does not restore it.
- **Documents**: PDF only (`%PDF-` magic bytes), ≤5 MB, multipart needs `GraphQL-Preflight: 1`. Bytes live in `DocumentContent`. Sent documents can't be deleted. Each generation adds a new document. `origin`: UPLOADED, TAILORED, BASE_CV.
- **Companies**: `createCompanies` is all-or-nothing, 1–100 rows, matches org.nr then name, and only fills blanks, so research can be re-pasted. `updateCompanyDetails` is a full replace of research fields. Org.nr uses mod-11. Favorite, avoided and notes have their own mutations so clients can't wipe them.
- **Contacts**: per company, name unique; found by name and fill-only from parses and research, never removed. Outreach log: `Contact.lastInteractionAt`/`followUpOn` copy the latest interaction, so a reply without a date clears the follow-up.
- **Parsing** (`Features/Parsing`): `parseListing` limit 200k for QUICK, else 100k; FULL and COMPANIES send the first 12k. Runs inline, returns the finished job. `parseListingFromUrl` reads the saved listing's url; `parseUrl` takes one from the client (not QUICK).
- **Page fetching** (`ListingPageFetcher`): public addresses only, checked per connection including redirects; 5 redirects, 2 MB, 10 s including the body; regexes time out at 5 s; charset from header or `<meta>`. JSON-LD `JobPosting` wins, else stripped text. Any failure → `InvalidParseRequestError`. JS-rendered sites fail as unreadable.
- **Prompts** (`Features/Prompts` + each feature): copy-paste and built-in paths share text. Bump a prompt's `Version` when its text or output changes. Allowlists: date of birth, nationality, work authorization, street address and salary never reach an LLM; CV prompts also omit contact details. [Applications guide](MASTER_Norwegian_Software_Developer_Applications.md).
- **AI safeguards**: every AI mutation takes optional `requestId`; one active operation per user; startup marks stale work interrupted. Daily limits apply to personal providers only. Failed batches stop dispatch on systemic codes (`LlmErrorCodes.StopsBatch`). [Guide](llm-call-safeguards.md).
- **Provider keys and models**: Owner-only keys, encrypted, validated before saving, retired rows keep no secret. Guests get only the shared Gemini default. A model id is only accepted from the resolver's allowlist. [Guide](llm-provider-keys.md), [UI notes](ui-ai-integration.md).
- **Usage**: Pacific days, because Gemini's limit resets at midnight Pacific. Shared totals sum all users; personal totals only the caller's credential. Missing prices → null, never 0.
- **Profile** (`Features/Profiles`): never null, loaded whole with server-side list order. Updates are full replaces; address, preferences and story have their own mutations. Months stored as the 1st. Tagging a skill adds it to the profile at level 1; untagging never removes it. `experienceMonths` counts experiences only, overlaps once. `clearProfileSections` is one transaction and keeps personal details, links and preferences.
- **Bullets and references**: positions stay consecutive from 0; ids survive edits and moves. Bullets ≤500, ≤20 per entry.
- **Bullet refinement**: `refineProfileBullets` scope = all, `kind`+`entryId`, or `bulletId` (else `InvalidProfileError`, before any AI call). Suggestions `{bulletId, text, note}`: text held to its own bullet's numbers and skill names, else null; English notes ≤300 ask for missing facts. Style from content-language writing samples + the other bullets. Copy-paste: `profileBulletRefinementPrompt` + `previewProfileBulletRefinement` (same checks). `applyProfileBulletRefinement` is not fact-checked.
- **Translations**: stale = source copy differs from the original, so editing the original changes nothing stored. `contentLanguage` is frozen while translations exist. AI drafts only missing or stale items; `apply*` saves as machine-made.
- **CV import**: English only, nothing saved until `importProfileCv`, which is atomic and takes reviewed selections. [Guide](profile-cv-import.md).
- **Skills**: per-user lookup shared by listings, companies and the profile. `cleanUpSkills` merges, splits and removes in one transaction, moves all links, and rewrites only link ids in drafts and base CV versions, never text. `deleteSkill` only works on unused skills. Translations are for word skills; products print as is.
- **Skill categories**: user-owned, `SortOrder` orders developer CV lines. AI suggestions only fill an uncategorized skill.
- **Job role categories**: [guide](job-role-categories.md). Listings filter on `jobRole.categoryId`; suggestions never replace an assigned category.
- **Tailored CV drafts**: one snapshot per listing; profile edits never rewrite it. Language: requested ?? saved draft ?? posting ?? English. Templates: DETAILED, CONCISE. DETAILED keeps full bullets reworded with listing terms (word-origin guard); CONCISE writes and merges one-line bullets from each entry (fact guard: numbers and skill names). Failing bullets fall back to source text, CONCISE paragraphs to null. Prompts adapt to listings outside the candidate's field. Section order: STANDARD (NAV), DEVELOPER. [Guide](tailored-cv-drafts.md), [formatting](norwegian-cv-formatting.md).
- **Base CVs**: separate stream, sent as-is, immutable numbered versions per language; unchanged content returns the latest version. In use → can't be deleted. External prompt (v7) `purpose`: `ROLE` (default) or `OPEN_APPLICATION` (market target); notes widen scope and outrank preferences, optional transition clause. Reached through a version, `baseCv` lists every version. [Guide](base-cvs.md).
- **Cover letters**: the server owns the header; the model writes only subject, greeting, body and closing. Language: letter draft → CV draft → posting → English. Pasted letters are split without AI. [Guide](norwegian-cover-letter.md).
- **Requirement map** (`Features/RequirementMaps`): one saved map per listing of what it needs, the profile evidence (bullet ids, entry refs; no skill ids) and the gaps. Not called analysis: `AnalyzedAt` means parsed. Saves are strict; AI candidates are corrected instead (unknown ids dropped, unsupported match → GAP). References are a snapshot. No prompt reads it yet. [Guide](requirement-map.md).
- **Application questions**: form text boxes, ≤20 per listing. One AI call (or external prompt) answers the whole form, so answers don't overlap; saved answers go along as drafts. Saved answers must fit `charLimit`; AI candidates are flagged instead, with one shorten retry for the over-limit ones. Length = JS `value.length`.
- **Dashboard**: `dashboardLayout` always returns every widget; widget data comes from existing queries, except `LLM_USAGE`.
- **Listing imports**: scanner reads `<Folder>/<account email>/<yyyy-MM-dd>[_HH-mm-ss]-<kind>.json`, read-only, dedupes by file name. Invalid files are stored with `error`. Listings are only created through quick add.
- **Training programs**: optional 1:1 on a listing; listing company = provider, employer may be unnamed. Filter with `isTrainingProgram`.

## Data rules
- Enums stored as strings. UTC `DateTime`; `DateOnly` for dates. Global filter `UserId == CurrentUserId` on every entity (`QueryFilterTests` guards it); child rows filter through their parent.
- FKs: user cascades to all user tables; Listing → Company/JobRole NoAction so a user delete cascades in one statement.
- `Names.Normalize` = Trim + ToUpperInvariant. `Location` is aliased in `GlobalUsings.cs` because Hot Chocolate has its own.
- JSON columns are leaf data: profile links, CV draft and base CV version content, cover letter content.
- Lossy `Down`, restore a backup instead: `MergeRollingDeadlines`, `AddCompanyIndustryAndLocations`, `AddProfileBullets`, `NavCvLanguages`, `DropCoverLetterPlace`, `AddListingKinds`, `BaseCvVersionLanguages`, `DropAiOperationWorkDigest`, `AddRequirementMaps`.

## Config keys
- `ConnectionStrings:Default`: `Data Source=jobhunt.db` (dev), `/data/jobhunt.db` (Docker)
- `Seed:OwnerEmail`, `Seed:OwnerPassword`: required, password re-applied on startup. `Seed:GuestEmail`: guest has no password. `Seed:Users:N:Email`/`Password`: optional extra Owner accounts
- `Llm:Gemini:ApiKey` (required), `DefaultModel`, `Models:[{Id, Label, RequestsPerDay?}]` (the allowlist); `Llm:Pricing:{model}`
- `Llm:Providers:{OpenAI|Mistral}`: `{Id, Label, MaxOutputTokens, ContextWindowTokens}`; `Llm:ProviderPricing:{provider}:{model}`
- `ListingImports:Folder` (default `listing-imports`; Docker `/data/listing-imports`), `ListingImports:PollSeconds` (default 60)
- `DataProtection:KeyRingPath`: default `<content root>/.local/dp-keys`, Docker `/data/dp-keys`; back up ring and DB together

## Stack
- .NET 10, EF Core + SQLite, Hot Chocolate 16 (source-generated types), Microsoft.Extensions.AI with Google.GenAI and OpenAI clients. Versions in `Directory.Packages.props`; `dotnet-ef` in `dotnet-tools.json`
- PDF: QuestPDF, embedded Source Sans 3; PdfPig in tests checks extracted text
- Tests: `ApiFactory` = in-memory SQLite per factory; `factory.Llm`, `factory.Providers`, `factory.Pages` fakes; `client.GraphQLAsync` fails on top-level errors, `client.GraphQLErrorAsync` returns the message
- Docker: `docker compose up` → :8080, non-root, volume `jobhunt-data:/data`

## Decisions log
- 2026-11-21 — CV prompts (base v7, tailored external v23, built-in analysis) are tiered: essential rules, then preferences with reasons, then output, with the task restated after the profile; shared rules say "the target", listing-only rules stay out of base CVs. No per-role bullet caps in either template (space is a 1-2 page / one-page preference); skills ~25-30 (`CONCISE` 15-20) and always include stack foundations; headline is a pattern, never a copyable example.
- 2026-11-21 — Base CVs for open applications are a prompt `purpose`, not a new stream: the target is a market, not a job ad, so no requirement framing; notes may add areas (e.g. backend) as relevant. Prompt-only; preview, data and tailored CVs unchanged.
- 2026-11-16 — Non-English listings keep their description twice: the parser (v20) writes it in the ad's language first, then translates. Prompts writing in the posting language get the original so CVs and letters echo the ad's wording; the UI keeps showing English. Old listings fill in on re-analysis.
- 2026-11-12 — Removed the Anthropic provider and SDK. Supported providers are Gemini, OpenAI and Mistral; obsolete keys are hidden, saved unavailable defaults resolve to Gemini, and explicit removed-model selections fail before inference.
- 2026-11-10 — CV work gets one spine: profile is a dump, a reviewed per-listing requirement map states the goal, then CVs and letters are written against it. The map ships first as its own JSON-per-listing slice with no consumers; evidence is bullets and entries, never skill ids, so skill cleanup has nothing to rewrite.
- 2026-11-10 — Profile bullet refinement checks each bullet against the CV research (delivery, own part, result) and returns notes asking for missing facts instead of inventing them; the fact guard uses only the bullet itself, so siblings can't be merged in.
- 2026-11-09 — Tailored CV bullets in both templates may take on listing terms, held by the source+listing word check (fallback, not rejection); `Shorten` → `SourceWords`, `ModeFor(listing:)`; base CVs unchanged.
- 2026-11-20 — Tailored CONCISE writes bullets from the profile as raw notes: merges related bullets per entry, condenses lists to what matters, ≤140 chars, in the candidate's style; a role of 24+ months gets the newest role's 5-bullet cap however old; server checks facts (numbers, skill names) against the whole entry instead of a 2-new-word limit, which silently reverted condensed bullets to the full original.
- 2026-11-08 — CV templates have one wording mode each (`Exact`/`Shorten`/`Rewrite`) checked by one shared function; base CV `DETAILED` is now exact like tailored drafts (no combining), and concise caps are single constants read by both code and prompt text.
- 2026-11-08 — Base CV `rewrite` flag: opt-in plain-language rewording guided by notes. Accuracy is held by a fact check (no number or profile skill name the entry's bullets lack) instead of the word check; meaning is left to the user's review.
- 2026-11-08 — CONCISE paragraphs are words from the selected bullets (≤4 new words, server-checked, else dropped) and follow `contentStyle`. Listings outside the candidate's field are judged by the model from listing + job titles (no field flag): own matching title leads, transferable bullets with tech cut, unrelated entries keep only title and dates.
- 2026-11-06 — Review pass: shared CV prompt rules live in `Features/Prompts`, moved with every prompt byte-identical; base CV and tailored prompts differ in wording, so merging more needs a real prompt change and version bump.
- 2026-11-06 — Listing lengths capped (names 200, description 20k); `ProtectedWorkDigest` dropped because nothing read it; `ParseJobStatus.Queued/Retrying` commented out until the queue exists.
- 2026-11-06 — Queries report bad input as GraphQL errors, because typed errors are mutation-only here. STATUS is an index: `schema.graphql` is the contract, code the authority, guides the detail.
- 2026-11-05 — Base CV AI drafting is copy-paste only: targetRole + private notes instead of a listing; picks from the master profile and combines less relevant bullets of one entry, kept honest by a per-entry word check rather than exact-text matching.
- 2026-11-04 — Base CV languages are per-version histories under one base CV (not separate CVs, not synced); a new language is prefilled deterministically from profile translations via source ids, no AI, with leftover text reported for manual fixing.
- 2026-11-03 — Generated listing files are imported as pending files, not listings: quick add stays the one create path (URL dedupe, AI enrichment); account = subfolder named by email; polling over FileSystemWatcher (WSL).
- 2026-10-31 — Base CVs are a separate stream from tailored CVs (sent as-is; editing for a listing = tailored draft, no link); every changed save is a new immutable version, documents record origin + version for outcome analysis; API name `BaseCv` (`template` = DETAILED/CONCISE layout).
- 2026-10-31 — Applications-guide pass is prompt-only (no migration): answers get work months, home/target cities and work modes but never salary/work authorization (placeholders instead); CV headline is an AI suggestion (null keeps); recruiter/broker are company kinds, the listing link stays separate.
- 2026-10-30 — Outreach is a loose per-contact interaction log (follow-up derived from the latest entry, stored on Contact for SQL filtering), not an application status; a conversation that becomes a job is an ordinary listing linked to the contact. Company research brings contacts through the listing's fill-only upsert.
- 2026-10-29 — CV language switch = optional `language` arg on generation calls, one draft per listing (switching regenerates; no per-language drafts, no migration).
- 2026-10-26 — Tailored CV templates: CONCISE for a 15-second recruiter scan beside unchanged DETAILED, one draft per listing (template passed per AI call); AI shortens bullets but a server word-origin check, not length validation, keeps the user's wording.
- 2026-10-24 — Form text boxes are `ApplicationQuestion` rows, not a cover letter mode; char limit stored (server measures, models can't count), saving over it rejected, AI candidates flagged instead; one shorten retry per generation.
- 2026-10-23 — Company research arrives as pasted JSON through `createCompanies` (no API token auth); repeat rows match org.nr then name and only fill blanks, so manual edits win; size as bands, not headcount.
- 2026-10-23 — Open applications are Listings with a Kind (reuse status/docs/letters/CVs); unsolicited has no Url/PostedOn/deadline; one active open application per company is advised, not enforced (possibleDuplicates).
- 2026-10-20 — Tailored CV follows the Norwegian IT CV research: project kind stored on the profile (not inferred from text); per-entry technologies only in the developer layout and trimmed per listing, not the whole inventory; certifications show validity; prompts forbid added metrics, inferred seniority and tool-equivalence claims.
- 2026-10-19 — Norwegian profile via AI translation of the English original (not a separate Norwegian profile): voice from target-language writing samples + current translations; only missing/stale items drafted so hand-made ones survive.
- 2026-10-16 — Skill categories became a user table (skills page edits them): seeded with the former 8 + Norwegian names (migration mapped skills and draft JSON), descriptions feed AI prompts by name, drafts store ids so renames reach PDFs; grouping only on DEVELOPER CVs; AI suggestions never overwrite a set category. Merges keep the target's own levels.
- 2026-10-15 — Tailored CV skills = listing matches + core stack (over-tailored lists hid the candidate's stack); added TESTING and AI categories (stored as strings, no migration; existing skills not recategorized); projects analyzed like experiences so AI work reaches AI-focused CVs.
- 2026-10-15 — Cover letters: guided story sections on Profile (not one blob or tagged snippets); writing samples sent raw as few-shot (no distilled style guide); AI/external/paste return an unsaved candidate like tailored CVs; the saved tailored CV is the candidate source when present.
- 2026-10-15 — Cover letter layout follows Telemark's traditional letter (no Norwegian greeting) but the owner wants the date alone under the sender block with Tekna's sizes; server owns header fields and the model writes only subject/greeting/body/closing; pasted letters are split server-side, no AI.
- 2026-10-14 — CV dates stay NAV words, not `MM.YYYY–d.d.`; developer section order is a per-draft toggle, not inferred from free-text role categories.
- 2026-10-14 — Skill translations only for word skills (products print as is), on the shared lookup; bullet wording fixes run in the content language and leave translations stale.
- 2026-10-14 — Skill cleanup merges/splits Skill lookup rows (one FK move fixes profile, tags, listings); LLM only proposes the plan, user reviews, server applies. Bullet wording consistency is a separate step.
- 2026-10-13 — CV follows NAV's format per draft language; CV languages use NAV's written/spoken words scale and any name (listings keep CEFR, which employers state).
- 2026-10-13 — BYOK: one active key per Owner/provider, protected credentials with persisted key ring; immutable credential usage history, shared Gemini only for absent Gemini key, personal failures never switch key/provider automatically.
- 2026-10-13 — AI safety: durable request identities and pre-dispatch usage admission, one active operation/user, opt-in personal-provider limits; shared Gemini exempt and systemic batch failures stop dispatch.
- 2026-10-13 — Profile wipe is one `clearProfileSections` mutation over CV sections, not per-item loops in the UI: atomic, server-owned; personal details/links/preferences are stable and never wiped.
- 2026-10-12 — Tailored CV analysis covers EDUCATION the same way as EXPERIENCE (bullet selection by `sourceEntryId`); it was missing from the original cut, which only had EXPERIENCE/SKILLS/SUMMARY.
- 2026-10-12 — QuestPDF Community licence for this personal project: render saved draft snapshots in C# without a browser; each call creates a new document to preserve uploads and earlier CVs.
- 2026-10-11 — URL preflight treats every saved attempt as seen for automated discovery; closed-only URLs still allow deliberate reapplication. Unsaved URLs are remembered only within the current batch/run by the caller.
- 2026-10-09 — Contacts are people per company linked to listings (one recruiter covers several ads), upserted by name through `ListingInput` with fill-empty merge so a parse never clobbers edits; quick prompt skips them.
- 2026-10-09 — CV and cover letter are optional on apply: form-only portals send no file and old applications may have lost theirs; the note says how you applied (no channel enum until stats need it).
- 2026-10-09 — Training programs (academies/bootcamps) are listings with an optional 1:1 `TrainingProgram`; listing company = provider so CompanyId stays required and the employer may be unnamed.
- 2026-10-08 — One master profile per user instead of several CVs; tailored CVs are selections from it. Employer is free text (not a Company); months as DateOnly on the 1st; skill years computed from tagged jobs, never stored; preferences as rows (matchable), links/highlights as JSON.
- 2026-10-08 — Entry skill tags add to `ProfileSkill` one way at level 1 (the level is required and a tag has none; `setProfileSkill` raises it); untagging never removes the profile skill, so a level you set is never lost. Hobbies are a list column; references are rows with ids so a CV can pick some.
- 2026-10-08 — Profile bullets are rows (one table, four nullable FKs + CHECK, one id space) instead of JSON/description, so a later per-listing draft or AI pick can refer to a bullet; ids survive edits and moves, so editing an entry never rewrites bullets.
- 2026-10-08 — Translation staleness is computed from a stored copy of the source text (`SourceText` etc.) instead of a timestamp/flag: nothing to update when the original is edited and the UI can show what changed. `contentLanguage` is frozen while translations exist so no source ends up in the wrong language.
- 2026-10-07 — Company avoid is a flag (`AvoidedAt`) plus a general `Notes` field, not one reason field: notes also hold good things and survive un-avoiding. Own mutations, since `updateCompany` is a full replace.
- 2026-10-06 — Usage: own count from `LlmUsage` (Gemini has no quota API; daily limit resets at midnight Pacific); shared users summed, per-model limit; personal credentials isolated; no daily token budget (Google only limits tokens per minute). Pacific-day logic in `PacificDay`; Docker tz data unchecked.
- 2026-10-05 — A listing is one attempt: reposted ad = new listing, Url unique among open (non-final) ones via partial index, earlier attempts linked by Url (no FK). ACCEPTED ends a successful attempt.
- 2026-10-05 — Effective due date computed in the query (no stored column), server-owned so the UI's "closing soon" and badges match the sort.
- 2026-10-05 — Listing search on a stored NormalizedTitle + lookups' NormalizedName (SQLite LIKE/lower()/NOCASE only fold ASCII); no full-text search.
- 2026-10-05 — Company industry is a per-user lookup (filterable, AI output settles on your names), one per company; locations are a join table with no head office.
- 2026-10-05 — ROLLING merged into ASAP: same meaning in practice, the distinction only gave the parser a no-op choice.
- 2026-09-27 — Documents: PDFs as SQLite BLOBs (one volume/backup, cascades, user filter) tied to one listing; immutable, sent ones undeletable. Download is the one REST domain endpoint (GraphQL can't serve binary).
- 2026-09-27 — Listings are archived, not deleted, once applied (sent document); tracking status will join that rule.
- 2026-09-27 — Duplicates decided server-side: normalized Url unique; same company + role/title returned as possibleDuplicates.
- 2026-09-27 — Tracking: stored Listing.Status + StatusChange timeline (user-set OccurredAt); interviews are rows, not statuses; undo instead of free status edits.
- 2026-09-27 — Applying is the only way to mark documents sent; file names re-dated to applied date; ContentHash for CV consistency.
- 2026-09-27 — Gemini via official Google.GenAI SDK (has `AsIChatClient`); `parseListing` runs inline first with the final async contract, so the UI can ship before the queue.
- 2026-09-27 — Notes are Markdown strings (UI sanitizes on render), not HTML, so the server never sanitizes.
- 2026-09-26 — Plan agreed (see docs/PLAN.md). Logger is a file + `/log-progress` skill, not an agent, to save tokens.
- 2026-09-26 — Auth: `AddIdentityCookies` + `requiresTwoFactor` in login contract so SSO/2FA stay additive; no email verification until public (PLAN §3, §9).
- 2026-09-26 — Hybrid API: REST for auth/health, GraphQL (Hot Chocolate 16) for domain data, subscriptions over SSE replace the custom SSE broker (PLAN §6–7). Package added with the first GraphQL feature.
- 2026-09-26 — Cookie `jobhunt.auth`: HttpOnly, SameSite=Lax, Secure except in Development; lockout 5 tries/5 min; login limiter 10/min per IP.
- 2026-09-26 — Listings before tracking. Listing title kept as in the ad; core role is a separate English `JobRole` (seniority + tech split out) so NO/EN ads line up.
- 2026-09-26 — Skills/companies/roles are per-user lookups (guest can't pollute; nightly reset just deletes rows). Listing skill level 1–5, no 0 (missing CV row = 0).
- 2026-09-26 — Deadline = enum (Date/Asap/Unspecified) + DateOnly; EmploymentType and Schedule split because NO ads combine them ("Fast, deltid").
- 2026-09-26 — Locations: city + ISO country code (UI names via Intl.DisplayNames), many per listing, country-only rows for remote; `IsTarget` for dashboard widget.
- 2026-09-26 — Parsing via external AI: API serves the prompt, UI stitches ad + prompt, user pastes JSON back to pre-fill the form. The server parser uses the same prompt. Skill level rubric: higher of years/wording.
- 2026-09-26 — Listings use offset paging (numbered pages in UI); HC cursors on IQueryable are offsets anyway. Lookups keep cursors (autocomplete).

## Next up
- Before public deploy: guest caps on shared Gemini, uploads and server-side fetching; forwarded headers for the rate limiter; `IsPublic` should reject NAT64, 6to4, `192.0.0.0/24` and `198.18.0.0/15`; `PooledConnectionLifetime` on the shared LLM client; Nitro off outside development.
- Check `/usr/share/zoneinfo/America/Los_Angeles` exists in the Docker aspnet image (usage queries need it).
- Cover letters: revise loop, save letter as sample; Nuxt UI (incl. application questions).
- CV import review UI in Nuxt; Norwegian import/translation matching and API-side file extraction remain deferred.
- CV synthesis rework reading the requirement map: bullets written from the whole entry (several → one), one fact guard with review flags instead of three wording modes, voice from example bullets and approved CV text. Then templates on the same rules plus copy-into-draft, and the cover letter reading the map.
- Deterministic profile ↔ listing matching (skills on SkillId, `experienceMonths` vs `MinYears`, roles, work mode).
- Nuxt BYOK/model/usage and optional personal-limit settings; UUID-per-action handling and SKIPPED partial analysis.
- Parse queue: `Channel` + `ParseWorker` (run `job.Model`), per-model RPM limiter, `onParseJobUpdated` over SSE; restore `Queued`/`Retrying`. Then the posting cache.
- General listing notes. Recruitment agencies: listing → recruiter/end-client link.
- Dashboard: counts by status, ghosted, upcoming interviews; outcomes per CV origin and base CV version; `topSkills`; target-cities widget.
- Favorites: drag-to-rank or named lists only if wanted.
