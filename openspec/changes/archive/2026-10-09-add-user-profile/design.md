# Design

## Context

See proposal.md for why. Relevant existing pieces:
- Per-user lookups `Skill`, `JobRole`, `Location` (found or created by `Names.Normalize`; `LocationLookup.FindOrCreateAsync`
  for locations). `Skill` is already commented as "shared by listings now and CVs later".
- Listing enums `WorkMode`, `EmploymentType`, `Language`, `CefrLevel` (in `Features/Listings/Listing.cs`).
- `LookupSeeder` loops over every user at startup and adds missing rows: the place to create profiles.
- Per-item CRUD with typed errors already exists for interviews (`InterviewMutations`): the pattern to copy.
- Global query filter `UserId == CurrentUserId` on user-owned tables. Child/join rows are filtered via their parent.

## Goals / Non-Goals

**Goals:**
- Relational schema that CV ↔ job matching can join on later (`SkillId`, `JobRoleId`, `Language`) without changes.
- Leave room for CV variants later: a variant can reference profile entry ids, so entries get stable int ids.

**Non-Goals:**
- No profile versioning or history.
- No `UpdatedAt` timestamps on entries (nothing reads them).
- No new language values in the `Language` enum. Adding one later is an additive enum change.

## Decisions

### Entities (`Features/Profiles`)

```
Profile                      UserId (unique), FullName?, Headline?, Summary? (Markdown), Email?, Phone?,
                             LocationId? → Location (SetNull), Links (JSON [{label,url}]),
                             MinSalary? (int), SalaryCurrency?, NoticePeriodMonths?
├─ ProfileJobRole            PK (ProfileId, JobRoleId)          ← target roles
├─ ProfileWorkMode           PK (ProfileId, WorkMode)
├─ ProfileEmploymentType     PK (ProfileId, EmploymentType)
├─ ProfileExperience         Id, UserId, Employer, Title, StartMonth, EndMonth?, Description?, Highlights (JSON string[])
│   └─ ProfileExperienceSkill  PK (ExperienceId, SkillId)
├─ ProfileEducation          Id, UserId, School, Degree?, Field?, StartMonth, EndMonth?, Description?
├─ ProfileCertification      Id, UserId, Name, Issuer?, IssuedMonth?, ExpiresMonth?, Url?
├─ ProfileProject            Id, UserId, Name, Description?, Url?, StartMonth?, EndMonth?
│   └─ ProfileProjectSkill     PK (ProjectId, SkillId)
├─ ProfileSkill              PK (ProfileId, SkillId), Level 1–5
└─ ProfileLanguage           PK (ProfileId, Language), Level (CefrLevel)
```

- **Entries carry `UserId`** (project rule: every user-owned entity has one, so the global filter covers it). Join and
  level tables (`ProfileSkill`, `*Skill` tags, preferences) have composite keys and are reached through their parent,
  like `ListingSkill`. Everything cascades from `AppUser`/`Profile`, and join rows cascade from `Skill`/`JobRole` too, like
  `ListingSkill` (lookups are never deleted on their own). Tag and role joins are EF skip navigations
  (`List<Skill> Skills`), so GraphQL exposes `skills { name }` directly.
- **Preferences are join tables, not JSON**: work modes, employment types, and roles will be matched against listings
  (rule: JSON only for leaf data never filtered or matched). `Links` and `Highlights` are JSON: display-only leaves.
- **Months are `DateOnly` on the 1st** (alternative: `"YYYY-MM"` strings or year + month ints). `DateOnly` keeps
  ordering and comparisons native in SQLite and EF, and the GraphQL `LocalDate` scalar already exists. The server
  normalizes in one helper (`new DateOnly(d.Year, d.Month, 1)`).
- **Employer is free text** (owner decision): past employers stay out of the target-companies list and its counts.

### Profile row created up front

`LookupSeeder` adds a `Profile` per user that lacks one, the same "add missing rows" pass it runs for lookups. New users
(the extra seeded accounts) get one on the next startup, and that's the only way users are created. Alternative: lazy
find-or-create in every mutation, with the query faking an empty object. Rejected: two code paths, and a query that
writes. Mutations can then load the profile with `db.Profiles.SingleAsync()` (the global filter scopes it).

### GraphQL shape

- `profile` → `Profile` loaded whole (`Include`s, split query) and sorted in C# per the spec. Not projected:
  HC projection can't apply a server-side order to nested lists, and the profile is a few dozen rows. The basics and
  preferences mutations return the same loaded profile.
- `Profile.targetLocations` is a resolver over `db.Locations.Where(l => l.IsTarget)`, not a relation. `IsTarget` stays
  the single source.
- `ProfileSkill.experienceMonths`: computed with a **DataLoader** keyed by `SkillId`. It loads `(SkillId, StartMonth,
  EndMonth)` for the user's tagged experiences in one query, then merges intervals in C#. Month-interval merging isn't
  practical in SQLite SQL, and a profile has dozens of rows, not thousands. "Now" comes from `TimeProvider`.
  Alternative: store months on `ProfileSkill`. Rejected: it goes stale when entries change.
- Mutation names are prefixed `Profile*` (`addProfileExperience`, `setProfileSkill`, …) to avoid clashes with future
  listing-level types (`Project`, `Education` are generic names).
- Inputs reuse the shapes of existing inputs: `LocationInput` for location, skill and role **names** (found or created
  like `ListingInput`), not ids.

### Errors

- `InvalidProfileError` for all validation (one exception, `InvalidProfileException(message)`), like `InvalidListingError`.
- `ProfileEntryNotFoundError` for unknown or foreign entry ids, skills not on the profile, and languages not on the
  profile. **One** error instead of four `*NotFound` types: the UI handles them the same way, and the message names the
  kind (`"Experience 12 was not found."`). This departs from the per-entity pattern (`InterviewNotFoundError`) on purpose.
- Bad country code: the existing `InvalidLocationError` from `LocationLookup`.

### Validation

One `Validate` method per input record in `ProfileMutations`, as in `ListingMutations`. Limits are in the specs.
Markdown max reuses `ApplicationMutations.MaxNoteLength` (10,000). Email is checked with `MailAddress.TryCreate`.
Currency is checked against the set of `RegionInfo.ISOCurrencySymbol` from specific cultures, built once (no package;
same approach as country codes via `RegionInfo`).

### File layout

`Features/Profiles/` (plural like the other slices; `Profile` would clash with the class name): `Profile.cs` (Profile, preferences joins, configs), `ProfileEntries.cs` (experience, education,
certification, project, tag tables, configs), `ProfileSkill.cs` (skill + language), `ProfileQueries.cs`,
`ProfileMutations.cs` (basics + preferences + skills + languages), `ProfileEntryMutations.cs` (the four entry kinds).
Splitting the mutations keeps each file readable. No shared base class for the four entry kinds: their fields differ,
and the CRUD bodies are short.

## Risks / Trade-offs

- [The `Language` enum has only English and Norwegian] → enough for the owner now. Adding values is additive to the
  schema and the DB (strings).
- [Profile-created skills and roles inflate the lookup lists with `listingCount: 0`] → intended: matching needs one
  shared id. The UI can filter on `listingCount` if needed.
- [~18 new mutations make the schema bigger] → chosen per-item CRUD (owner decision) for small edits and precise
  errors. Names are grouped by the `Profile` prefix.
- [Guest profile data persists until the guest sandbox reset exists] → same as all other guest data today. The reset
  deletes by user, and cascades cover the profile.
- [The DataLoader computes months per request] → negligible at profile size.

## Migration Plan

1. `make migration name=AddProfile` (backs up `jobhunt.db` first). New tables only, no data changes.
2. On startup the migration applies, then `LookupSeeder` inserts one empty `Profile` per existing user.
3. Rollback: `Down` drops the tables. Restore the `*.db.bak` if needed.
