# Job Hunt Assistant API — Plan

A hobby backend for tracking job applications, with LLM-assisted job-posting parsing.
Frontend: Nuxt / Vue (separate repo). Not production-grade, but it should be clean enough
that other developers browsing GitHub can find their way around.

## 1. Decisions from the interview

| Topic | Decision |
|---|---|
| Runtime | .NET 10 (LTS), **Minimal APIs** (learning goal: modern .NET coming from .NET Framework 4) |
| Database | SQLite + EF Core 10 |
| Auth | ASP.NET Core Identity + **HttpOnly cookie** auth. Two seeded accounts: Owner (you) and Guest |
| Guest account | Sandbox: full CRUD, data reset nightly, AI calls heavily capped |
| MVP scope | Tracker: applications + status history, notes, contacts, CVs, dashboard stats |
| AI in MVP | Job-posting parsing and English CV text import; both support built-in AI and external copy-paste prompts |
| AI later | CV ↔ job matching, PDF/image parsing, more providers (Claude, OpenAI) |
| LLM providers | Gemini `gemini-3.1-flash-lite` (free tier) by default; Ollama optional. **Chosen per user**. No Ollama container: if Ollama can't be reached, fail cleanly |
| API style | **Hybrid**: REST for auth + health, **GraphQL (Hot Chocolate)** for all domain data. No endpoint exists in both |
| Realtime | Parse progress as a GraphQL subscription over SSE (`graphql-sse`) |
| Token saving | Hash-based cache, structured JSON output, input trimming |
| Usage | **Track only, no quotas** for Owner: daily trends, model, tokens, estimated cost. Guest keeps a small daily cap |
| Hosting | Docker (docker-compose) on a VPS or home server; SQLite on a volume |
| CV storage | One structured master profile per user in the DB (see §4); tailored CVs are generated from it later |

## 2. Solution layout

A vertical-slice layout: code is grouped by feature, not by technical layer. It is easy to learn,
easy to navigate on GitHub, and doesn't have the ceremony of a four-project Clean Architecture setup.

```
job-hunt-assistant-api/
├─ src/JobHunt.Api/
│  ├─ Program.cs                  # composition root, kept short
│  ├─ Features/
│  │  ├─ Auth/                    # login, logout, guest login, /me
│  │  ├─ Applications/            # CRUD, status changes, history
│  │  ├─ Notes/
│  │  ├─ Contacts/
│  │  ├─ Profiles/               # master profile: basics, preferences, history, skills
│  │  ├─ Dashboard/
│  │  ├─ JobPostings/             # parse request, results, queue worker
│  │  ├─ Usage/                   # LLM usage stats
│  │  └─ Settings/                # per-user provider/model preference
│  ├─ Llm/                        # provider abstraction, Gemini/Ollama, prompt, trimming, cache
│  ├─ Data/                       # AppDbContext, migrations, seeding
│  └─ Common/                     # auth policies, ProblemDetails, extensions
├─ tests/JobHunt.Api.Tests/       # xUnit + WebApplicationFactory + in-memory SQLite
├─ docs/PLAN.md
├─ Directory.Build.props          # nullable, warnings-as-errors, LangVersion
├─ Directory.Packages.props       # central package management
├─ .editorconfig
├─ Dockerfile
├─ docker-compose.yml
├─ .github/workflows/ci.yml       # build + test
└─ README.md                      # setup, architecture sketch, screenshots of Scalar
```

REST features (Auth) have an `XxxEndpoints.cs` with a `MapXxx(this IEndpointRouteBuilder)` extension
that uses `MapGroup("/api/xxx")`. GraphQL features have `XxxQueries.cs` / `XxxMutations.cs`
(`[QueryType]` / `[MutationType]` extensions, source-generated registration). EF entities live
next to their feature.

**Conventions**
- REST: built-in OpenAPI + **Scalar** UI at `/scalar`.
- GraphQL: Hot Chocolate at `/graphql` (Nitro IDE on GET in dev). The Nuxt app generates TS types
  with `graphql-codegen` from the schema. Resolvers return `IQueryable` with
  `[UsePaging]/[UseProjection]/[UseFiltering]/[UseSorting]` so each query becomes one SQL query.
  DbContext is registered so Hot Chocolate can run resolvers in parallel (pooled/factory).
- Schema file: `/graphql` requires login, so codegen can't introspect it. Export with
  `RunWithGraphQLCommandsAsync` (`dotnet run -- schema export`) to a committed `schema.graphql`;
  CI fails if it's stale. The UI's codegen reads that file.
- .NET 10 built-in minimal-API validation (`AddValidation()` + DataAnnotations).
- Errors: `ProblemDetails` for REST. GraphQL uses Hot Chocolate mutation conventions (typed
  `errors` on the mutation payload for expected failures, e.g. not found / validation).
- `TimeProvider` injected everywhere, not `DateTime.UtcNow`, so time can be controlled in tests.
- Secrets: `dotnet user-secrets` in dev, environment variables in Docker. The API key never reaches the UI.

## 3. Authentication

- `AddIdentityCore<AppUser>()` + `SignInManager` + cookie scheme. **Custom endpoints** instead of
  `MapIdentityApi()`, because that one exposes `/register` and we want registration closed.
- Roles: `Owner`, `Guest`. Both users are seeded at startup from config
  (`Seed:OwnerEmail`, `Seed:OwnerPassword`). The guest has a fixed, public login.
- Cookie: `HttpOnly`, `Secure`, `SameSite=Lax`. Return 401/403 on API calls instead of redirecting.
- **Nuxt integration:** proxy `/api/**` and `/graphql` through Nuxt (`routeRules`)
  so the browser treats API and UI as same-origin. Cookies and SSE subscriptions then work with no CORS
  or `SameSite=None` hacks. Allow CORS with credentials only as a dev fallback.
- Built-in rate limiter on the login endpoints.

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/login` | email + password → cookie |
| `POST /api/auth/guest` | one-click guest login (for the demo button) |
| `POST /api/auth/logout` | |
| `GET /api/auth/me` | id, email, role, provider preference |

**Guest sandbox:** a `GuestResetService` (`BackgroundService`) wipes the guest's rows at a
configurable time each night and re-seeds sample data (a handful of applications, a CV, some
parsed postings). Guest AI cap: `Guest:DailyLlmRequests` (default 5). Cache hits don't count.

**SSO / 2FA later (no migration needed):** Identity's schema already has `AspNetUserLogins`
(external logins) and `AspNetUserTokens` (authenticator key, recovery codes). Three M1 choices keep
these additive: `AddIdentityCookies()` (registers the External + TwoFactor schemes), login response
carries `requiresTwoFactor`, and SSO only links to existing users (provider key, then verified
email; never auto-create). Guest may never enable 2FA, change password, or link SSO.
Seeded users are `EmailConfirmed = true`; no email sending until registration opens.

## 4. Data model

All user-owned tables have `UserId`. A global EF query filter scopes every query to the current
user, so one user can never read another's data even if an endpoint forgets to filter.
`docs/STATUS.md` lists every column; this is the shape and the reasons.

```
AppUser (Identity) ── UserSettings (PreferredProvider, PreferredModel)      ← planned (M5)

Company (Name, NormalizedName, LocationId?)    ┐ per user, found-or-created by name,
JobRole (Name, NormalizedName)  ← core role, EN │ unique (UserId, NormalizedName)
Skill   (Name, NormalizedName)  ← shared w/ profile ┘
Location (City?, NormalizedCity ("" if none), CountryCode ISO 3166, IsTarget)  unique (UserId, CountryCode, NormalizedCity)
  City null = whole country ("Norway, anywhere" for remote). IsTarget → dashboard "jobs in your target cities".

Listing = one application attempt                ← built
  Id, UserId, Title (as in the ad), NormalizedTitle, CompanyId, JobRoleId, Seniority?, EmploymentType?,
  Schedule? (FullTime/PartTime), WorkMode?, Description?, Url (required), PostedOn (required, DateOnly),
  DeadlineType (Date/Asap/Unspecified), DeadlineOn?, PostingLanguage?,
  FavoritedAt?, ArchivedAt?, AnalyzedAt?, Status, StatusChangedAt?, CreatedAt, UpdatedAt
  ├─ ListingSkill     (SkillId, Level 1–5, MinYears?, IsRequired)   ← no 0: a CV without the skill has no row
  ├─ ListingLanguage  (Language, Level A1–C2?, IsRequired)
  ├─ ListingLocation  (LocationId)   ← several offices; remote-anywhere has none
  ├─ StatusChange     (Status, OccurredAt (user-set, UTC), Note? Markdown)   ← the timeline
  ├─ Interview        (Title?, ScheduledAt UTC, Notes? Markdown)
  ├─ Document         (Kind Cv/CoverLetter, FileName, ContentHash, SentAt?) + DocumentContent (BLOB)
  └─ later: Note, Contact (M2)
  Enums are stored as strings. Due date (DATE → DeadlineOn, ASAP → PostedOn+14, UNSPECIFIED → PostedOn+30)
  is computed in the query, never stored.

Status lives on the listing
  Saved → Applied (needs a CV document; marks documents sent) → Interviewing (first interview)
        → Offer → Accepted / Rejected / Withdrawn;  Applied/Interviewing → Rejected / Withdrawn
  Final ("closed") = Rejected, Withdrawn, Accepted. Archive is a separate flag and doesn't close a listing.
  Changes go through apply/recordOutcome/undo, never a free status edit, so the timeline stays true.

Reposted ads
  A reposted ad is a new listing (new attempt). Url is unique per user among open listings only
  (partial index); earlier closed attempts are found by Url, no FK.

Documents belong to one listing: the PDF sent with that attempt. Bytes live in a separate table
so listing queries never load them. Counts (per company/role/skill/location) are computed with
DataLoaders, never stored.

Profile = one per user, created by LookupSeeder   ← built (M3), relational
  FullName?, Headline?, Summary? (Markdown), Email?, Phone?, LocationId? → Location,
  Links (JSON: Kind GITHUB/LINKEDIN/PORTFOLIO/OTHER, Label only for OTHER, Url), Hobbies (JSON list),
  ContentLanguage (ENGLISH default: the language the user's own text is written in)
  DateOfBirth?, Nationality?, WorkAuthorization?   ← sensitive: an AI CV prompt builder must use a field allowlist
  MinSalary?, SalaryCurrency? (ISO 4217), NoticePeriodMonths?
  ├─ ProfileJobRoles / ProfileWorkModes / ProfileEmploymentTypes   ← preferences, rows so listings can match on them
  ├─ ProfileExperience    (Employer free text, Title, LocationId?, StartMonth, EndMonth?, Skills)
  ├─ ProfileEducation     (School, Degree?, Field?, LocationId?, StartMonth, EndMonth?)
  ├─ ProfileCertification (Name, Issuer?, IssuedMonth?, ExpiresMonth?, Url?)
  ├─ ProfileProject       (Name, Url?, LocationId?, StartMonth?, EndMonth?, Skills)
  ├─ ProfileSkill         (SkillId → shared Skill lookup, Level 1–5)  + computed experienceMonths from tagged jobs
  ├─ ProfileLanguage      (Language, Level A1–C2)
  ├─ ProfileReference     (Name, Relationship, Company?, Email?, Phone?, Note?, Position)
  ├─ ProfileBullet        (Text, Position; belongs to exactly one experience/education/certification/project)
  │    └─ ProfileBulletTranslation (Language, Text, SourceText, IsMachine; unique per bullet + language)
  └─ ProfileTranslation   (Language, Headline?, Summary?, SourceHeadline?, SourceSummary?, IsMachine; unique per language)
  Bullets are the only free text on an entry. They are rows with ids, not JSON, so a later per-listing draft or AI
  pick can refer to them; one table, four nullable FKs + CHECK, so there is one id space.
  Translations: the original stays where it is and a translation hangs off its id. Each stores a copy of the text it
  was made from, and "stale" is computed by comparing that copy with the current text, so editing the original has
  nothing to update. A translation exists only in the language other than ContentLanguage, which can't change while
  any translation exists. Titles, degrees, entry names, references and hobbies are not translated.
  Tagging a skill on an experience or project adds it to ProfileSkill at level 1 if missing (a tag has no level;
  setProfileSkill raises it). Untagging or removing the profile skill never touches the other side.
  Months are DateOnly on the 1st; no end = current. Target cities stay on Location.IsTarget.
  Skills, roles and languages use the same lookups and scales as listings, so profile ↔ job matching is a join.
  One master profile instead of several CVs: data is entered once; tailored CVs are selections from it later.

English CV import accepts pasted plain text/Markdown using configured Gemini, or a copyable external prompt with no CV input.
Both return candidates for review; `importProfileCv` saves explicit selections atomically into an empty or populated
English-primary profile. It preserves omitted basics/entries, entry and bullet ids, and translations (source edits make
them stale). Existing-entry structural updates still use full field values; no automatic matching or deduplication.
Sensitive details/references and preferences are excluded from extraction and untouched by import. Raw pasted text can
contain personal information; the API does not claim to redact it, but retains no raw CV parse job. See [integration guide](profile-cv-import.md).

ParseJob                           ← built (runs inline until the queue, M6)
  Id (Guid), UserId, Mode (Full/Quick), RawText, Status (Queued/Running/Retrying/Succeeded/Failed),
  Result? (JSON text), Error?, Provider, CreatedAt, StartedAt?, CompletedAt?

JobPosting                         ← planned (M6): parse cache, deduplicated
  Id, ContentHash (unique with PromptVersion), PromptVersion, ParsedJson, Provider, Model, CreatedAt
  ParseJob gets JobPostingId? when it lands.

LlmUsage                           ← built: one row per LLM call (cache hits too, once the cache exists)
  Id, UserId, Provider, Model, Operation ("parse-listing"), InputTokens?, OutputTokens?,
  LatencyMs, Success, ErrorCode? (invalid-json/rate-limited/provider-error/timeout), CreatedAt
```

## 5. LLM layer

**Abstraction: `Microsoft.Extensions.AI` (`IChatClient`).** It is the standard .NET abstraction,
and adding Claude/OpenAI later means registering another client, not writing a new code path.

- Providers are registered as **keyed services** (`"gemini"`, `"ollama"`, later `"anthropic"`, `"openai"`).
- `LlmProviderResolver` picks the client from `UserSettings.PreferredProvider`, with config as fallback.
- **Gemini client:** decide at scaffold time between the official Google GenAI .NET SDK (if its
  `IChatClient` adapter is mature) and Gemini's OpenAI-compatible endpoint through the OpenAI
  `IChatClient`. Both support JSON-schema output. *To verify: the exact model id in AI Studio
  (it may carry a `-preview` suffix). It will be config-driven either way.*
- **Ollama** (`OllamaSharp`, implements `IChatClient`): before use, a cached health check
  (`GET /api/tags`, 2s timeout, result cached ~30s). If unreachable → `ProviderUnavailable`
  → the ParseJob goes `FAILED` with a readable error (pushed via `onParseJobUpdated`).
  No container; `Llm:Ollama:BaseUrl` is optional config.
- Every call goes through a wrapper that writes an `LlmUsage` row (tokens from the response's
  `UsageDetails`).

### Parse pipeline (token-frugal)

```
pasted text
  → Normalize: collapse whitespace, strip boilerplate (cookie/EEO/"apply now" regex list),
               cap at Llm:MaxInputChars (≈12k chars ≈ 3k tokens)
  → SHA-256(normalized) + PromptVersion
  → cache hit?  yes → link existing JobPosting, log CacheHit usage, done (0 tokens)
                no  → one LLM call, structured output, low temperature
  → validate JSON against the C# record → save JobPosting → done
```

- **One call does everything**: fields + language detection + language requirements + summary.
- Short system prompt, schema-constrained output (no prose), `maxOutputTokens` capped.
- Bump `PromptVersion` whenever the prompt or schema changes; old cache rows then stop matching.
- The cache is global across users (postings aren't private), so a guest pasting a demo posting
  you already parsed costs nothing.

**Output schema:** matches `ListingInput` (minus `url`) so a result pre-fills the add-listing form.
The prompt lives in `Features/Listings/ListingParsePromptQueries.cs` and is served as `listingParsePrompt`
for the copy-paste path (external AI). The server-side parser reuses it.
- **Open:** the prompt embeds the user's own role/skill names, so its output is per-user. The cache above
  must then key on user too (or strip names from the cached prompt and match names after parsing),
  otherwise one user's names could surface in another's result.

## 6. Background queue + subscriptions

```
mutation parseListing(input: { text, mode: FULL | QUICK })
  → create ParseJob(Queued) → write to Channel<Guid> → returns { parseJob { id status } }

ParseWorker (BackgroundService)
  → reads the channel; a TokenBucket rate limiter paces calls to the free-tier RPM (config)
  → on 429: respect Retry-After, requeue with backoff (max N tries)
  → publishes each state change via Hot Chocolate's ITopicEventSender (in-memory provider)

subscription onParseJobUpdated → ParseJob { id mode status error result }
  → transport: SSE (`graphql-sse` protocol) over the same cookie; WebSockets not needed
  → topic keyed by UserId so users only see their own jobs
```

Statuses: `QUEUED`, `RUNNING`, `RETRYING`, `SUCCEEDED` (with `result`), `FAILED` (with `error`).

- `mode` picks the prompt: FULL = `listingParsePrompt` (one object), QUICK = `listingQuickParsePrompt` (array).
- `result` is the validated JSON text in exactly the shape the copy-paste path produces, so the UI feeds
  both paths into the same `JSON.parse` → form/table code. Contract given to the UI on 2026-09-27.
A cache hit goes straight to `SUCCEEDED`.

- On startup, `Queued`/`Running` ParseJobs are re-enqueued, so the queue survives restarts.
- If the UI misses an event, it can query `parseJob(id)`.
- Single instance only (in-memory subscriptions); fine for SQLite + one container.
- A missing Ollama → `FAILED` with an error code (was: REST 503 + `parse.failed`).

## 7. API surface (MVP)

**REST**
```
Auth       POST /api/auth/login, /guest, /logout · GET /api/auth/me   (see §3)
Health     GET  /api/health
Documents  GET  /api/documents/{id}   (PDF bytes; GraphQL can't serve binary)
```

**GraphQL** (`/graphql`, requires login). Built names match `schema.graphql`; `(planned)` = not built yet.
```
Query
  listings(archived, dueWithinDays, search, where, order, skip/take) → offset page of Listing
  listingById(id)                           → Listing { skills languages locations documents
                                                        statusHistory interviews dueOn }
  companies, jobRoles, skills, locations    → cursor-paged lookups (search, listingCount)
  listingParsePrompt, listingQuickParsePrompt → prompt for copy-paste into an external AI
  parseJob(id)
  profileCvParsePrompt                       → { version instructions } (no CV input)
  profile                                   → Profile { location links jobRoles workModes employmentTypes targetLocations
                                                        experiences educations certifications projects skills languages references }
  dashboardStats                            (planned, M4) → counts by status, applications/week (last 12w),
                   response rate, avg days to first response, upcoming interviews,
                   ghosted = Applied/Interviewing with no status change or interview for N days (derived, never stored)
  usageDaily(from, to), usageSummary        (planned, M5) → calls, cache hits, tokens in/out, est. cost
  usageToday                                (planned, M5) → token-monitor widget: requests, cache hits, tokens in/out,
                                              est. cost, limits (RPD/RPM from config), remaining
                                              (Guest: daily cap; Owner: RPD − requests). Own count;
                                              Gemini has no remaining-quota API
  settings                                  (planned, M5) → preferred provider/model, available providers + health

Mutation
  createListing, updateListing, deleteListing, setListingFavorite, setListingArchived
  updateCompany, saveLocation
  uploadDocument, deleteDocument
  applyToListing, recordOutcome, updateStatusChange, undoStatusChange   (the only way status changes)
  addInterview, updateInterview, deleteInterview
  parseListing(text, mode)
  add/update/deleteNote, add/update/deleteContact   (planned, M2)
  updateProfile, updateProfilePreferences
  parseProfileCv(text), previewProfileCvImport(json), importProfileCv(selection)
  add/update/deleteProfileExperience|Education|Certification|Project
  set/removeProfileSkill, set/removeProfileLanguage
  add/update/move/deleteProfileBullet, add/update/move/deleteProfileReference
  setProfileContentLanguage, set/deleteProfileBulletTranslation, set/deleteProfileTranslation
  updateSettings                                     (planned, M5)

Subscription
  onParseJobUpdated                         (planned, M6; §6)
```

**Estimated cost:** the free tier costs $0, so usage stats compute a "would cost" figure from a
price table in config (`Llm:Pricing:{model}:InputPerMTok/OutputPerMTok`). You fill in the
current paid-tier prices, and the numbers update without code changes.

## 8. Docker

- Multi-stage `Dockerfile` (SDK build → `aspnet` runtime image, non-root user).
- `docker-compose.yml`: `api` service only; volume for `/data/jobhunt.db` and DataProtection keys
  (so cookies survive restarts); env vars for secrets. No Ollama service.
- EF migrations applied at startup (acceptable for a hobby project).

## 9. Milestones

| # | Milestone | Done when |
|---|---|---|
| M0 | Install .NET 10 SDK in WSL; scaffold solution, props files, CI, Scalar, EF Core + SQLite, Dockerfile + compose | `dotnet test` + `docker build` green in CI |
| M1 | Identity + cookie auth, seeded Owner/Guest, user query filter | can log in as both via Scalar |
| M2 | Listings + application tracking (status, timeline, interviews, documents) + notes + contacts | CRUD + history tested; notes/contacts still pending |
| M3 | Profile (relational tables, see §4) | profile CRUD tested; profile skills/languages/roles use the listing lookups |
| M4 | Dashboard stats | stats endpoint tested with seeded data |
| M5 | LLM layer: Gemini client, Ollama health handler, usage logging, settings | one parse call works end-to-end synchronously |
| M6 | Parse queue + SSE + cache + trimming + retries | UI receives events; repeat paste = cache hit |
| M7 | Guest sandbox: nightly reset, sample data, AI cap | guest resets, capped at N calls |
| M8 | Deploy to VPS, DataProtection keys volume, README polish (incl. "Design decisions": why vertical slices, why no repository layer, why cookies over JWT, token-saving strategy) | runs on the VPS |

**After MVP:** profile ↔ job match, CV generation from the profile (renders a CV as a PDF `Document` on a listing, so
`applyToListing` sends it like an upload; any LLM step gets only allowlisted profile fields, never date of birth,
contact details or references; generation takes a target language and uses the translation where one exists, and a
later auto-translate writes translations one bullet at a time with `isMachine` set), PDF/image posting input, Claude/OpenAI providers,
URL scraping, maybe Postgres. English CV import is built; its Nuxt review UI, Norwegian import/translation matching,
and API-side CV file extraction remain follow-ups.

**Deferred follow-ups** (found in the project review, not built yet):
- Guest AI cap (M7, must land before M8 deploy): guests can call `parseListing` on the owner's
  free-tier quota today.
- User scoping for background work (M6 parse worker, M7 guest reset): the global user filter reads
  the user from the HTTP request, which a background job doesn't have, so it needs another way to
  set the current user.
- Store identical CV files once (M3): the same CV uploaded to several listings is stored once per
  listing today; `Document.ContentHash` already identifies duplicates, so content can be shared by hash.

**If it ever goes public (open registration):**
- Auth (small): register endpoint, email confirmation + forgot password (needs an email provider
  like Resend/Postmark + SPF/DKIM DNS), SSO/2FA as in §3.
- LLM cost: per-user daily caps. Build M7's guest cap per-user/per-role, not guest-only.
- Abuse: CAPTCHA (Turnstile) + rate limits on register/forgot-password.
- Privacy/GDPR: delete-account + export-data endpoints.
- Keep it cheap meanwhile: authorize by role/policy, never by a specific email; per-user settings
  in the DB; every user-owned entity cascade-deletes from `AppUser`; Guest is a normal user + role.

## 10. Open questions (can be decided during build)

1. Contacts: per application (current plan), or a shared contact list linked to many applications?
2. Guest daily AI cap: is 5 right? And the nightly reset time?
3. Future providers: your keys only (server config), or bring-your-own-key per user?
4. The exact Gemini model id and free-tier RPM/RPD limits: read them from AI Studio and put them in config.
