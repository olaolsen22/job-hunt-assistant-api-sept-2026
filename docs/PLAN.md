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
| AI in MVP | **Job-posting parsing only**, from pasted text; includes language detection, language requirements, and an English summary |
| AI later | CV ↔ job matching, PDF/image parsing, more providers (Claude, OpenAI) |
| LLM providers | Gemini `gemini-3.1-flash-lite` (free tier) by default; Ollama optional. **Chosen per user**. No Ollama container: if Ollama can't be reached, fail cleanly |
| API style | **Hybrid**: REST for auth + health, **GraphQL (Hot Chocolate)** for all domain data. No endpoint exists in both |
| Realtime | Parse progress as a GraphQL subscription over SSE (`graphql-sse`) |
| Token saving | Hash-based cache, structured JSON output, input trimming |
| Usage | **Track only, no quotas** for Owner: daily trends, model, tokens, estimated cost. Guest keeps a small daily cap |
| Hosting | Docker (docker-compose) on a VPS or home server; SQLite on a volume |
| CV storage | Structured data in the DB (see §4). *Assumption: "actual data" = structured fields, not a blob* |

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
│  │  ├─ Cvs/
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

```
AppUser (Identity) ── UserSettings (PreferredProvider, PreferredModel)

Company (Name, NormalizedName, LocationId?)    ┐ per user, found-or-created by name,
JobRole (Name, NormalizedName)  ← core role, EN │ unique (UserId, NormalizedName)
Skill   (Name, NormalizedName)  ← shared w/ CVs ┘
Location (City?, NormalizedCity ("" if none), CountryCode ISO 3166, IsTarget)  unique (UserId, CountryCode, NormalizedCity)
  City null = whole country ("Norway, anywhere" for remote). IsTarget → dashboard "jobs in your target cities".

Listing                                          ← built
  Id, UserId, Title (as in the ad), CompanyId, JobRoleId, Seniority?, EmploymentType?,
  Schedule? (FullTime/PartTime), WorkMode?, Description?, Url (required), PostedOn (required, DateOnly),
  DeadlineType (Date/Asap/Rolling/Unspecified), DeadlineOn?, PostingLanguage?, CreatedAt, UpdatedAt
  ├─ ListingSkill     (SkillId, Level 1–5, MinYears?, IsRequired)   ← no 0: a CV without the skill has no row
  ├─ ListingLanguage  (Language, Level A1–C2?, IsRequired)
  └─ ListingLocation  (LocationId)   ← several offices; remote-anywhere has none
  Enums are stored as strings.

Application tracking                             ← built
  Listing.Status (Saved/Applied/Interviewing/Offer/Rejected/Withdrawn) + StatusChangedAt (for list filter/sort)
  ├─ StatusChange (Status, OccurredAt (user-settable, UTC), Note? Markdown)   ← the timeline
  ├─ Interview    (Title?, ScheduledAt UTC, Notes? Markdown)
  ├─ Document.SentAt + ContentHash (SHA-256: same CV across applications)
  │    Saved → Applied (needs a CV) → Interviewing (first interview) → Offer/Rejected/Withdrawn
  ├─ later: Note, Contact
  Counts (per company/role/skill/location) are computed with DataLoaders, never stored.
Favorites: Listing.FavoritedAt DateTime? ← built. Later if wanted: FavoriteRank? (drag-to-rank); named lists → ListingList + items table.

Cv
  Id, UserId, Name, IsDefault, FullName, Headline, Summary, Email, Phone, Location, Links[]
  ├─ Experiences[]    (Company, Title, Start, End?, Description, Highlights[])
  ├─ Educations[]     (School, Degree, Field, Start, End?)
  ├─ Skills[]         (Name, Level?, Category?)
  ├─ Languages[]      (Language, Proficiency)
  └─ Certifications[] (Name, Issuer, Date?)
  → Relational tables with FKs. Skills and Languages must be comparable with JobPosting
    skills/language requirements for the CV ↔ job matching on the roadmap. db-architect designs this
    in M3 (e.g. a shared Skill lookup table). JSON columns only for leaf data like Highlights[] and Links[].

JobPosting                         ← cache + parsed result, deduplicated
  Id, ContentHash (unique with PromptVersion), PromptVersion, NormalizedText,
  ParsedJson, Title, Company, PostingLanguage, Provider, Model, CreatedAt

ParseJob                           ← queue item, one per user request
  Id, UserId, JobPostingId?, RawText, Status (Queued/Running/Succeeded/Failed),
  Error?, Provider, CreatedAt, StartedAt?, CompletedAt?

LlmUsage                           ← one row per LLM call attempt (and per cache hit)
  Id, UserId, Provider, Model, Operation ("parse-posting"), CacheHit,
  InputTokens, OutputTokens, LatencyMs, Success, ErrorCode?, CreatedAt
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
Auth     POST /api/auth/login, /guest, /logout · GET /api/auth/me   (see §3)
Health   GET  /api/health
```

**GraphQL** (`/graphql`, requires login) — outline, names settle during build
```
Query
  applications(where, order, first/after)   → Connection<Application>  (search, status filter, sort)
  application(id)                           → Application { notes contacts statusHistory jobPosting }
  cvs, cv(id)                               → Cv { experiences educations skills languages certifications }
  jobPostings(...), jobPosting(id), parseJob(id)
  dashboardStats → counts by status, applications/week (last 12w), response rate,
                   avg days to first response, upcoming follow-ups,
                   ghosted = Applied/Interviewing with no status change or interview for N days (derived, never stored)
  usageDaily(from, to), usageSummary        → calls, cache hits, tokens in/out, est. cost
  usageToday                                → token-monitor widget: requests, cache hits, tokens in/out,
                                              est. cost, limits (RPD/RPM from config), remaining
                                              (Guest: daily cap; Owner: RPD − requests). Own count;
                                              Gemini has no remaining-quota API
  settings                                  → preferred provider/model, available providers + health

Mutation
  create/update/deleteApplication, changeApplicationStatus (writes history row)
  createApplicationFromPosting(jobPostingId)
  add/update/deleteNote, add/update/deleteContact
  create/update/deleteCv, setDefaultCv
  parseJobPosting(text)
  updateSettings

Subscription
  onParseJobUpdated                         (§6)
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
| M2 | Applications + status history + notes + contacts | CRUD + history tested |
| M3 | Structured CVs | CV CRUD with JSON columns |
| M4 | Dashboard stats | stats endpoint tested with seeded data |
| M5 | LLM layer: Gemini client, Ollama health handler, usage logging, settings | one parse call works end-to-end synchronously |
| M6 | Parse queue + SSE + cache + trimming + retries | UI receives events; repeat paste = cache hit |
| M7 | Guest sandbox: nightly reset, sample data, AI cap | guest resets, capped at N calls |
| M8 | Deploy to VPS, DataProtection keys volume, README polish (incl. "Design decisions": why vertical slices, why no repository layer, why cookies over JWT, token-saving strategy) | runs on the VPS |

**After MVP:** CV ↔ job match, PDF/image posting input, Claude/OpenAI providers,
URL scraping, maybe Postgres.

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
