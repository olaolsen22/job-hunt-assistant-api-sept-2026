# Project Status

Updated: 2026-10-06 · Last logged commit: fa64f04

## Milestones
| # | Milestone | Status |
|---|---|---|
| M0 | Scaffold, CI, Scalar, EF Core + SQLite, Docker | ✅ done |
| M1 | Identity + cookie auth, Owner/Guest seed | ✅ done |
| M2 | Listings (+ companies, job roles, skills, languages); then tracking: status history, notes, contacts | 🟡 listings, locations, documents, archive, application tracking (status, interviews) done; notes/contacts pending |
| M3 | Structured CVs | ⬜ |
| M4 | Dashboard stats | ⬜ |
| M5 | LLM layer, usage logging, settings | 🟡 Gemini client, usage logging, inline `parseListing`, `llmUsageToday`/`usageDaily` done; settings/Ollama pending |
| M6 | Parse queue + SSE + cache | ⬜ |
| M7 | Guest sandbox reset + AI cap | ⬜ |
| M8 | Deploy, README | ⬜ |

## Endpoints
All endpoints require auth (fallback policy) unless marked anon.
- `GET /api/health` (anon) — built-in health checks incl. DB connectivity
- `POST /api/auth/login` (anon, rate-limited) — `{email, password}` → `{requiresTwoFactor, user}`; 401 bad creds, 429 locked out
- `POST /api/auth/guest` (anon, rate-limited) — one-click guest sign-in, same response
- `POST /api/auth/logout` (anon) — 204
- `GET /api/auth/me` — `{id, email, role}`
- `GET /api/documents/{id}` — PDF bytes, `Content-Disposition: inline; filename*=<FileName>`, `nosniff`; 404 if not found/other user's
- Dev only (anon): `/openapi/v1.json`, `/scalar`
- `POST /graphql` (Hot Chocolate 16; Nitro IDE on GET in browser). All user data scoped by global query filter.
  - SDL committed at `schema.graphql` (repo root) for the UI; `SchemaTests` regenerates it and fails when it's stale
  - Query: `listings(archived: Boolean = false, dueWithinDays: Int, search: String)` (archived hidden by default; `search` = case-insensitive incl. æøå on title/company/job role name, not description, blank ignored, combines with `where`; `dueWithinDays: N` = today ≤ due ≤ today+N, expired excluded, negative → top-level error) / `companies(search)` / `jobRoles(search)` / `skills(search)` / `locations(search: city)` — projection, `where`, `order`; `listingById(id)`
  - Paging: `listings` = offset (`skip`/`take`, default 20, max 100) → `{ items pageInfo { hasNextPage hasPreviousPage } totalCount }` for numbered pages; lookups = cursor (`first`/`after`, default 10) → `{ nodes pageInfo totalCount }`
  - Default order only when no client `order` (`ISortingContext.IsDefined` + `Handled(false)`); listings: upcoming by due date asc, then expired by due date asc, then newest CreatedAt; client sorts get `Id` tie-breaker
  - Effective due date (`ListingQueries.WithDue`, one expression for order/filter/`Listing.dueOn`): DATE → `deadlineOn`, ASAP → `postedOn + 14`, UNSPECIFIED → `postedOn + 30`. Expired = due < today (UTC); affects only order/filter, never status/archive. Date ranges via `where: { postedOn/deadlineOn: { gte, lte } }` (deadline range matches DATE only)
  - `listingParsePrompt { version instructions }` — copy-paste prompt for external AIs (UI appends `<posting>…</posting>`); enums from C#, user's role/skill names (max 100 each), today's date. `ListingParsePromptQueries.Version` = 4, `QuickVersion` = 2
  - `parseListing(input:{text, mode: FULL|QUICK|COMPANIES})` → `{parseJob}` (runs inline for now: returns SUCCEEDED/FAILED; later QUEUED + `onParseJobUpdated`). Same prompts as `listingParsePrompt`/`listingQuickParsePrompt`, `<posting>` wrapped, JSON mode, temp 0, input capped 12k chars, max 8192 output tokens. `result` = JSON text (object for FULL, array for QUICK and COMPANIES; COMPANIES rows `[{name, industryName, locations:[{city, countryCode}]}]` are `createCompanies` input and nothing is saved) or FAILED "couldn't be read"; 429 → FAILED "free-tier limit". `parseJob(id: UUID)` (own jobs only). Error: `InvalidParseRequestError` (blank, >100k chars)
  - `companyParsePrompt { version instructions }` (`Features/Companies`, Version = 1) — copy-paste prompt for external AIs, same text the COMPANIES parse sends; lists the user's industries (max 100); name-only input may use the model's own knowledge, `null`/`[]` when unsure, so the UI previews rows before `createCompanies`
  - `Company|JobRole|Skill|Location.listingCount` — computed via DataLoader (one GROUP BY per page); `Company.applicationCount` (status ≠ SAVED)
  - `Listing.statusHistory(order)` / `Listing.interviews(order)` — nested sorting; `listings(where: {status: {eq: APPLIED}})` filters on status
  - Mutation (conventions, typed errors): `createListing(input:{listing})` → `{listing, previousAttempts, possibleDuplicates}` (previousAttempts = closed listings with same Url, newest first; possibleDuplicates = same company + same role or title, other Url; still saved), `updateListing(input:{id, listing})` (full replace incl. skills/languages; `isAnalyzed` keeps first AnalyzedAt, false clears), `deleteListing(input:{id})` → `id`, `updateCompany(input:{id, name, industryName?, locations: [LocationInput!]!})` (full replace: no `industryName` clears the industry, `[]` clears locations; **BREAKING** for the UI: `location` input and `Company.location` are gone), `createCompanies(input:{companies:[{name, industryName?, locations:[{city?, countryCode}]}]})` → `{results:[{company, created}]}` (1–100 rows, all-or-nothing in one transaction; existing name or repeat in the batch → that company untouched with `created: false`, even if the row's industry/locations differ; blank name/batch size → `InvalidCompanyError`, bad country code → `InvalidLocationError`, also on rows that match an existing company; repeated location in a row collapsed), `saveLocation(input:{city?, countryCode, isTarget})` (find-or-create), `setCompanyFavorite(input:{id, isFavorite})` → `Company` (same rules as the listing favorite; `CompanyNotFoundError`; `updateCompany`/`createCompanies` never touch it; shortlist = `companies(where:{favoritedAt:{neq:null}}, order:[{favoritedAt:DESC}])`), `setListingFavorite(input:{id, isFavorite})` / `setListingArchived(input:{id, isArchived})` (keep first timestamp; UpdatedAt untouched). `deleteListing` blocked (`ListingInUseError`) once status ≠ SAVED. Url normalized (no utm_*/fbclid/gclid, fragment, trailing /), unique per user among open listings → `DuplicateListingError { existingListingId }` (archived included; same Url as only closed listings = new attempt; a closed listing may take an open one's Url)
  - Documents: `uploadDocument(input:{listingId, kind, file: Upload})` (GraphQL multipart; needs `GraphQL-Preflight: 1` header; PDF only via `%PDF-` magic bytes, max 5 MB), `deleteDocument(input:{id})` → `id` (blocked while sent). Read via `Listing.documents`. Sent only via `applyToListing`
  - Applications: `applyToListing(input:{listingId, cvDocumentId, coverLetterDocumentId?, appliedAt?, note?})` (SAVED → APPLIED; marks docs sent, renames with applied date), `recordOutcome(input:{listingId, outcome: OFFER|ACCEPTED|REJECTED|WITHDRAWN, occurredAt?, note?})`, `updateStatusChange(input:{id, occurredAt, note?})` (fix date/note; re-dates sent docs if APPLIED), `undoStatusChange(input:{listingId})` (drops latest; undoing APPLIED unsends docs; INTERVIEWING blocked while interviews exist; undoing a final step → `DuplicateListingError` if another open listing has the Url) — all return `listing`
  - Transitions: SAVED→APPLIED→INTERVIEWING (first `addInterview`)→OFFER/REJECTED/WITHDRAWN; APPLIED→OFFER/REJECTED/WITHDRAWN; OFFER→ACCEPTED/REJECTED (offer withdrawn)/WITHDRAWN (declined). Final = closed: REJECTED/WITHDRAWN/ACCEPTED (`ApplicationStatuses.Final`), derived, archive doesn't affect it. Dates not future, nothing before applied date. Notes Markdown, trimmed, blank = null, max 10k
  - Interviews: `addInterview(input:{listingId, scheduledAt, title?, notes?})` (APPLIED/INTERVIEWING only), `updateInterview(input:{id, ...})` (any status), `deleteInterview(input:{id})` → `id`. Title max 200
  - Errors: `InvalidParseRequestError`, `InvalidListingError`, `ListingNotFoundError`, `InvalidCompanyError`, `CompanyNotFoundError`, `InvalidLocationError`, `ListingInUseError`, `DuplicateListingError`, `InvalidDocumentError`, `DocumentNotFoundError`, `DocumentSentError`, `InvalidStatusTransitionError`, `InvalidApplicationError`, `StatusChangeNotFoundError`, `InvalidInterviewError`, `InterviewNotFoundError`
  - `ListingInput` takes `companyName`/`jobRoleName`/skill `name`/`locations: [{city?, countryCode}]` → found case-insensitively or created. Defaults: skill `level: 3`, `isRequired: true`, `deadlineType: UNSPECIFIED`
  - `DeadlineType` = `DATE | ASAP | UNSPECIFIED` (ROLLING removed — BREAKING for the UI). Prompts map "Snarest"/"ASAP"/"straks"/"Løpende"/"fortløpende"/"rolling" → ASAP. ParseJob results stored before the change may still say `"ROLLING"`: UI maps it to ASAP or re-parses

## Entities
- `AppUser : IdentityUser` (string Id) + Identity tables (incl. UserLogins/UserTokens for future SSO/2FA)
- Roles: `Owner`, `Guest` (`Features/Auth/AppUser.cs`)
- `AppDbContext : IdentityDbContext<AppUser>` in `Data/`; configs via `IEntityTypeConfiguration` + `ApplyConfigurationsFromAssembly`. Migrations applied at startup, then `AuthSeeder`, then `LookupSeeder` (starter job roles, skills, Norwegian cities + country-only NO for every user; adds missing rows only).
- `Listing` (`Features/Listings/Listing.cs`): Title, NormalizedTitle (`Names.Normalize(Title)`, for search; not exposed), CompanyId, JobRoleId, Seniority?, EmploymentType?, Schedule?, WorkMode?, Location?, Description?, Url?, PostedOn (required, not future; UI pre-fills today) / DeadlineOn? (DateOnly), DeadlineType, PostingLanguage?, Url (required, http/https only), CreatedAt/UpdatedAt (UTC), FavoritedAt? (null = not favorited), ArchivedAt? (null = active), AnalyzedAt? (null = details still to fill in; user-set via `ListingInput.isAnalyzed`, not derived), Status (`ApplicationStatus`, default Saved; index (UserId, Status)), StatusChangedAt? (latest change's OccurredAt); (UserId, Url) partial unique index, filter `Status NOT IN ('Rejected','Withdrawn','Accepted')` (must match `ApplicationStatuses.Final`)
  - `ListingLocation` PK (ListingId, LocationId)
  - `ListingSkill` PK (ListingId, SkillId): Level 1–5, MinYears? (as stated in ad), IsRequired · `ListingLanguage` PK (ListingId, Language): Level CEFR?, IsRequired
- `Document` (`Features/Documents`): UserId, ListingId (cascade), Kind (Cv/CoverLetter), FileName (`{Company}_{JobRole}_{Kind}_{yyyy-MM-dd}.pdf`, non-alphanumerics → `-`), OriginalFileName, SizeBytes, ContentHash (SHA-256 hex; index (UserId, ContentHash)), CreatedAt, SentAt?. Bytes in `DocumentContent` (PK DocumentId, BLOB, cascade) so they never load with listings
- `StatusChange` (`Features/Applications`): UserId, ListingId (cascade), Status, OccurredAt (user-set, UTC), Note? (Markdown), CreatedAt. Ghosted is derived later, not a status
- `Interview` (`Features/Applications`): UserId, ListingId (cascade), Title?, ScheduledAt (UTC; input `DateTime` with offset), Notes? (Markdown), CreatedAt/UpdatedAt; index (ListingId, ScheduledAt)
- `ParseJob` (`Features/Parsing`): Guid Id, UserId, Mode, Status (Queued/Running/Retrying/Succeeded/Failed), RawText (not exposed), Result?, Error?, Provider, CreatedAt, StartedAt?, CompletedAt?
- `LlmUsage` (`Llm/`): UserId, Provider, Model, Operation (`parse-listing` / `parse-companies`), InputTokens?/OutputTokens?, LatencyMs, Success, ErrorCode? (`invalid-json`/`rate-limited`/`provider-error`/`timeout`), CreatedAt; index (UserId, CreatedAt). One row per LLM call (`ListingParser`)
- Lookups per user, unique (UserId, NormalizedName): `Company` (+FavoritedAt? null = not favorited, +IndustryId? SetNull; `Locations` via `CompanyLocation(CompanyId, LocationId)` join, both cascade, no head office), `Industry` (`Features/Industries`, found or created by name), `JobRole` (core role, English), `Skill`.
  - Dashboard (`Features/Dashboard`): `dashboardLayout` → `[{widget, isShown}]`, always all 10 widgets in the user's order (never saved = default order, all shown); `saveDashboardLayout(input:{widgets:[{widget, isShown}]})` → `{layout}` (list order = display order, full replace; missing/duplicate widget → `InvalidDashboardLayoutError`). Widget data is not served here: the UI builds it from `listings`/`companies`, except `LLM_USAGE` (queries below). **BREAKING** for the UI: saves must include `LLM_USAGE`
  - Usage (`Features/Usage`, any signed-in user, all users' calls summed because one Gemini key = one quota; `IgnoreQueryFilters`, totals only): `llmUsageToday` → `{requests, inputTokens, outputTokens, requestsLimit?, remainingRequests?, resetsAt}` (configured model only; since midnight Pacific; failed calls count as requests, null tokens as 0; limit from `Llm:Gemini:RequestsPerDay`, null if unset; `resetsAt` = next midnight Pacific, UTC); `usageDaily(days: Int = 30)` → `[{day, model, requests, inputTokens, outputTokens, estimatedCost?}]` (all models, Pacific days, days 1–90 else top-level error, no row for empty days; cost from `Llm:Pricing`, null without a price; the UI projects monthly cost)
  - Queries: `industries(search)` (ordered by name, paging/filter/sort); `Company.industry` / `Company.locations { location { city countryCode } }` (same shape as `Listing.locations`); `companies(where: {industry: {name: {eq}}})`
- `DashboardWidget` (`Features/Dashboard`): UserId, `Widget` (`DashboardWidgetKind`, 10 values, last `LLM_USAGE`, declaration order = default order), IsShown, Position; unique (UserId, Widget). Rows only after the first save; widgets without a row are appended shown. Not a lookup: the set is fixed in code
- `Location` (`Features/Locations`): City? (null = whole country), NormalizedCity ("" if none), CountryCode (ISO 3166, validated via `RegionInfo`), IsTarget; unique (UserId, CountryCode, NormalizedCity). `LocationLookup.FindOrCreateAsync` shared by listings/companies/saveLocation
- Hot Chocolate has a global `Location` type: files referencing ours use `using Location = JobHunt.Api.Features.Locations.Location;` `Names.Normalize` = Trim + ToUpperInvariant
- Enums stored as strings (`ConfigureConventions`). Global filter `UserId == CurrentUserId` (from `IHttpContextAccessor`); child rows filtered via Listing
- FKs: AppUser → all user tables cascade; Listing → Company/JobRole NoAction; children cascade from Listing/Skill
- Migrations: `AddSamples`, `RemoveSamples`, `AddIdentity`, `AddListings`, `AddLocations` (dropped free-text Location columns; no rows existed), `RequireUrlAddMinYears`, `RequirePostedOn` (backfills null PostedOn from CreatedAt), `AddListingFavorites`, `AddDocuments`, `AddListingArchive`, `AddListingAnalyzed`, `AddListingUrlIndex`, `AddApplicationTracking` (existing listings default `Saved`; no sent docs existed, no backfill), `AddParseJobsAndLlmUsage`, `AllowReapplyToListings` (Url index → partial; no data change), `MergeRollingDeadlines` (Rolling → Asap; Down is a no-op), `AddCompanyIndustryAndLocations` (copies `Companies.LocationId` into `CompanyLocations` before dropping it; Down keeps the first location per company), `AddCompanyFavorites` (nullable column, no backfill), `AddListingNormalizedTitle` (SQL backfill: `UPPER` + æøå `REPLACE`; other non-ASCII lowercase, e.g. é/ü, in existing titles only found by exact uppercase until the listing is saved again), `AddDashboardWidgets` (new table, no backfill)

## Config keys
- `ConnectionStrings:Default` — `Data Source=jobhunt.db` (dev), `/data/jobhunt.db` (Docker)
- `Seed:OwnerEmail`, `Seed:OwnerPassword` — required; dev defaults in `appsettings.Development.json` (override with user-secrets), `.env` → compose (Docker). Password re-applied on startup.
- `Seed:GuestEmail` — `guest@jobhunt.local`; guest has no password
- `Seed:Users:N:Email`, `Seed:Users:N:Password` — optional extra `Owner` accounts (e.g. a real account next to the test owner); user-secrets. Created if missing, password re-applied on startup, blank value → startup error
- `Llm:Gemini:ApiKey` — required (ValidateOnStart); user-secrets (dev), `GEMINI_API_KEY` in `.env` → compose (Docker). `Llm:Gemini:Model` — `gemini-3.1-flash-lite` in appsettings.json. `Llm:Gemini:RequestsPerDay` — optional, 500 in appsettings.json (free tier, copied from AI Studio's rate-limit page 2026-10-06; Gemini has no API for it). `Llm:Pricing:{model}:InputPerMTok` / `OutputPerMTok` — optional paid-tier prices for the cost estimate, none set

## Stack
- .NET SDK 10.0.112, Google.GenAI 1.22.0 (`AsIChatClient`, keyed `IChatClient` "gemini"), xunit 2.9.3, Scalar.AspNetCore 2.17.10, OpenApi/Mvc.Testing 10.0.12, HotChocolate 16.6.7 (AspNetCore, Data.EntityFramework, Types.Analyzers, Types.OffsetPagination)
- GraphQL types source-generated: `[assembly: Module("JobHuntTypes")]` → `AddJobHuntTypes()`. DbContext via `AddDbContextFactory` + `RegisterDbContextFactory`
- Versions pinned in `Directory.Packages.props`; shared props in `Directory.Build.props`
- `dotnet-ef` as local tool (`dotnet-tools.json`)
- Tests: `ApiFactory` = WebApplicationFactory + named in-memory SQLite per factory; `factory.Llm` = `FakeChatClient` replacing the Gemini client; `GetOwnerClientAsync`/`GetGuestClientAsync` (cached, login is rate-limited); `client.GraphQLAsync(query, variables)` fails on top-level errors
- Docker: `docker compose up` → :8080, non-root `app` user, volume `jobhunt-data:/data`

## Decisions log
- 2026-09-26 — Plan agreed (see docs/PLAN.md). Logger is a file + `/log-progress` skill, not an agent, to save tokens.
- 2026-09-26 — Auth: `AddIdentityCookies` + `requiresTwoFactor` in login contract so SSO/2FA stay additive; no email verification until public (PLAN §3, §9).
- 2026-09-26 — Hybrid API: REST for auth/health, GraphQL (Hot Chocolate 16) for domain data, subscriptions over SSE replace the custom SSE broker (PLAN §6–7). Package added with the first GraphQL feature.
- 2026-09-26 — Cookie `jobhunt.auth`: HttpOnly, SameSite=Lax, Secure except in Development; lockout 5 tries/5 min; login limiter 10/min per IP.
- 2026-09-26 — Listings before tracking. Listing title kept as in the ad; core role is a separate English `JobRole` (seniority + tech split out) so NO/EN ads line up.
- 2026-09-26 — Skills/companies/roles are per-user lookups (guest can't pollute; nightly reset just deletes rows). Listing skill level 1–5, no 0 (missing CV row = 0).
- 2026-09-26 — Deadline = enum (Date/Asap/Rolling/Unspecified) + DateOnly; EmploymentType and Schedule split because NO ads combine them ("Fast, deltid").
- 2026-09-26 — Locations: city + ISO country code (UI names via Intl.DisplayNames), many per listing, country-only rows for remote; `IsTarget` for dashboard widget.
- 2026-09-26 — Parsing via external AI: API serves the prompt, UI stitches ad + prompt, user pastes JSON back to pre-fill the form. Same prompt will drive the server parser. Skill level rubric: higher of years/wording.
- 2026-09-26 — Listings use offset paging (numbered pages in UI); HC cursors on IQueryable are offsets anyway. Lookups keep cursors (autocomplete).
- 2026-09-27 — Documents: PDFs as SQLite BLOBs (one volume/backup, cascades, user filter) tied to one listing; immutable, sent ones undeletable. Download is the one REST domain endpoint (GraphQL can't serve binary).
- 2026-09-27 — Listings are archived, not deleted, once applied (sent document); tracking status will join that rule.
- 2026-09-27 — Duplicates decided server-side: normalized Url unique; same company + role/title returned as possibleDuplicates.
- 2026-09-27 — Tracking: stored Listing.Status + StatusChange timeline (user-set OccurredAt); interviews are rows, not statuses; undo instead of free status edits.
- 2026-09-27 — Apply requires a CV; applying is the only way to mark documents sent; file names re-dated to applied date; ContentHash for CV consistency.
- 2026-09-27 — Gemini via official Google.GenAI SDK (has `AsIChatClient`); `parseListing` runs inline first with the final async contract, so the UI can ship before the queue.
- 2026-09-27 — Notes are Markdown strings (UI sanitizes on render), not HTML, so the server never sanitizes.
- 2026-10-05 — A listing is one attempt: reposted ad = new listing, Url unique among open (non-final) ones via partial index, earlier attempts linked by Url (no FK). ACCEPTED ends a successful attempt.
- 2026-10-05 — Effective due date computed in the query (no stored column), server-owned so the UI's "closing soon" and badges match the sort.
- 2026-10-05 — Listing search on a stored NormalizedTitle + lookups' NormalizedName (SQLite LIKE/lower()/NOCASE only fold ASCII); no full-text search.
- 2026-10-05 — Company industry is a per-user lookup (filterable, AI output settles on your names), one per company; locations are a join table with no head office.
- 2026-10-05 — ROLLING merged into ASAP: same meaning in practice, the distinction only gave the parser a no-op choice.

- 2026-10-06 — Usage: own count from `LlmUsage` (Gemini has no quota API; daily limit resets at midnight Pacific); all users summed, per-model limit; no daily token budget (Google only limits tokens per minute). Pacific-day logic in `PacificDay`; Docker tz data unchecked.

## Next up
- Parse queue: `Channel` + `ParseWorker`, RPM limiter, 429 retries, `onParseJobUpdated` over SSE; worker needs user scoping for the prompt's role/skill names. Then the posting cache.
- Check `/usr/share/zoneinfo/America/Los_Angeles` exists in the Docker aspnet image (usage queries need it); add `tzdata` if not.
- Guest AI cap before deploying (guests can call `parseListing` on the owner's free-tier quota now).
- General listing notes and contacts.
- Dashboard: counts by status, ghosted (no change/interview for N days), upcoming interviews.
- `topSkills` (GROUP BY skill: listingCount, requiredCount, averageLevel) with dashboard stats; target-cities widget uses existing `locations`/`listings` filters.
- Guest cap on document uploads (count/size) with the guest sandbox.
- Favorites: drag-to-rank (`FavoriteRank`) or named lists only if wanted.
- M8: forwarded headers (rate limiter IP behind proxy), DataProtection keys volume.
