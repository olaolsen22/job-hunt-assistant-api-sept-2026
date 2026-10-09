# Design

## Context

See proposal.md for motivation. `Company` has `Name`, `NormalizedName` (unique per user via `Names.Normalize`) and a
nullable `LocationId` (SetNull). Companies are created only inside `ListingMutations`, which finds a company by
`NormalizedName` or builds a new unsaved one. `LocationLookup.FindOrCreateAsync` queries the DB and otherwise returns a
fresh unsaved `Location`; `LocationLookup.Key` builds a `country|city` key, and `createListing` rejects a repeated
location in one listing. `JobRole` is the model for a per-user lookup (`Name`, `NormalizedName`, unique with `UserId`,
global query filter). `parseListing` stores a `ParseJob` with a `ParseMode` (`Full`, `Quick`), and `ListingParser`
picks the prompt by mode, sends the same prompt text that the copy-paste queries serve, and checks the JSON shape
(object for Full, array for Quick). Requirements are in `specs/companies` and `specs/company-parsing`.

## Goals / Non-Goals

**Goals:**
- All rules for creating and matching companies live in the API; the UI only collects and previews rows.
- A bulk create that is safe to repeat: re-pasting a list changes nothing.
- The AI path reuses the existing parse pipeline and the existing "AI proposes, user saves" flow.

**Non-Goals:**
- Deleting companies or industries, merging duplicates, or an industry hierarchy.
- Starter industries (the vocabulary grows from use, and the prompt lists the user's own names).
- More company fields (website, notes, contacts), which come with the notes/contacts work.
- Matching near-duplicate names ("Equinor" vs "Equinor Energy"); only normalized equality counts.
- Industry counts on the `Industry` type.

## Decisions

**One bulk mutation, atomic.**
`createCompanies` validates every row first (batch size, blank names, country codes), then builds all new rows and calls
`SaveChangesAsync` once, so SQLite wraps it in one transaction. One company is a list of one.
- `createCompany` looped by the UI: puts the order of operations and the handling of half-finished imports in the
  frontend; the project keeps data rules in the API.
- Per-row results with partial success: needs an error-per-row result shape; the UI can fix a bad row and resubmit an
  all-or-nothing batch just as easily, because existing names are returned, not rejected.

**Existing name = return it untouched.**
A row for an existing company returns it with `created: false` and ignores the row's industry and locations. Import
must not overwrite what the user edited; `updateCompany` is the way to change a company. Repeats inside a batch follow
the same rule (first row wins).
- Fill in blanks on the existing company: a merge rule the user would have to learn, for little gain.
- Error on duplicates: makes re-pasting a list fail on the first known company.

**Per-batch lookups for industries and locations.**
`FindOrCreateAsync` finds saved rows only, so two rows with the same new location would each get their own unsaved
`Location` and the unique index would fail on save. The mutation keeps a dictionary per batch (`LocationLookup.Key`
for locations, normalized name for industries and companies) and reuses the instance it already created. A small
private helper in `CompanyMutations` resolves `industryName` and locations for both `createCompanies` and
`updateCompany`, which are its two real uses.
- Save after each row: loses atomicity.
- Make `LocationLookup` check `ChangeTracker.Local`: changes shared code that listings and `saveLocation` also use,
  for a need only the bulk path has.

**Industry is a per-user lookup with a single FK on `Company`.**
`Industry` copies `JobRole`: `Name`, `NormalizedName`, unique `(UserId, NormalizedName)`, query filter, cascade from
the user. `Company.IndustryId?` uses SetNull, as the old location FK did. It is filterable and countable, and AI
output ("Oil & gas", "Energy") collapses to whatever names the user already has once the prompt lists them.
- Free-text string on `Company`: simplest, but "show me all energy companies" would depend on exact spelling.
- Many industries per company: not wanted yet; a join table can replace the FK later.

**Locations become a join table, with no head office.**
`CompanyLocation(CompanyId, LocationId)` with a composite key, both sides cascade, mirroring `ListingLocation`.
`Company.LocationId` is dropped.
`Company.locations` is exposed as the join rows (`locations { location { ... } }`), the same shape as `Listing.locations`,
so the UI handles both the same way.
- Keep head office plus the join table: two ways to hold a location, for a distinction the user doesn't need.

**Migration copies before it drops.**
Generate the migration with `make migration name=AddCompanyIndustryAndLocations`, then in `Up` add
`INSERT INTO CompanyLocation (CompanyId, LocationId) SELECT Id, LocationId FROM Companies WHERE LocationId IS NOT NULL`
after the table is created and before the column is dropped. SQLite rebuilds the `Companies` table for the drop, so
that order matters. `Down` recreates the column from the first location per company (best effort, as the data model
no longer holds one head office).

**AI path: one new parse mode and one prompt query.**
`ParseMode` gets `Companies` (enums are stored as text, so existing `ParseJob` rows are unaffected). `ListingParser` chooses the
prompt with a switch on the mode, expects an array for `Quick` and `Companies`, and logs operation `parse-companies`
so company parses can be told apart in `LlmUsage`. The prompt lives in a new `CompanyParsePromptQueries` beside the
company code, returns a new `CompanyParsePrompt(Version, Instructions)` record (reusing `ListingParsePrompt` would put
the wrong name in the GraphQL schema), and its output property names match `createCompanies` input so the UI passes the
array straight through. The class and file names `ListingParser` and `parseListing` stay; renaming them is a separate
change.
- A separate `parseCompanies` mutation: duplicates the text checks, limits, ParseJob handling and the later
  queue/subscription wiring.

**Prompt: text first, general knowledge for names only.**
The prompt tells the model to fill `industryName` and `locations` from the text. When the text is only names, it may
use general knowledge for companies it knows well and must use `null` when unsure. Industry names reuse the user's
list (max 100, like roles and skills). The model has no web access, so the UI previews every row before
`createCompanies`; the server cannot tell a guess from a fact.

**Limits.**
Batch size is 1 to 100 rows, comfortably above the ~30 rows a quick-add paste produces and far below anything that
stresses a single SQLite transaction. Parse text limits are the listing ones.

## Risks / Trade-offs

- [AI guesses wrong industries or locations for name-only input] → Prompt says `null` when unsure; the UI shows rows for review; nothing saves until `createCompanies`.
- [UI breaks: `updateCompany(location)` and `Company.location` go away] → Marked BREAKING in `docs/STATUS.md`; the UI is being built against this change.
- [Industry names fragment ("Oil & gas" vs "Energy")] → The prompt lists existing names to reuse; no merge tool yet, so a wrong name is fixed per company with `updateCompany`.
- [An existing company is not updated by a later import] → By design; `created: false` tells the UI so it can say "3 already existed".
- [Down migration loses all but one location per company] → Acceptable for a hobby project's rollback; documented in the migration.

## Migration Plan

1. Generate the migration, add the `INSERT ... SELECT`, apply with the usual startup migration.
2. The old `Company.LocationId` values are now `CompanyLocation` rows; no other data changes.
3. Roll back by reverting the migration; companies keep one location each.
