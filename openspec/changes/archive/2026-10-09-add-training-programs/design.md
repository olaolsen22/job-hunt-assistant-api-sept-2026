# Design

## Context

`Listing.CompanyId` is required and drives the FK, search, duplicate detection and document names. Child tables of a
listing carry no `UserId` and are filtered through their listing (`AppDbContext.cs:76-82`). `updateListing` replaces
the listing from `ListingInput`, which parse output feeds directly; `Contacts` already uses "null keeps" so old
clients don't wipe data (`ListingMutations.cs:32-33`). Company find-or-create is inline in `ApplyAsync`
(`ListingMutations.cs:213-214`). The quick prompt doesn't ask for `employmentType`, so it needs no change.

## Goals / Non-Goals

**Goals:** make programs a first-class, filterable part of a listing without touching the Listings table or existing
behaviour for normal listings.

**Non-Goals:** loan schemes, duration fields, provider-specific entities, avoid/favorite rules on the end employer,
quick-prompt detection of programs, UI work.

## Decisions

- **1:1 child table, not columns/enum/separate entity.** `TrainingProgram { ListingId (PK+FK), EndCompanyId?,
  StartsOn?, EndsOn?, IsPaid?, JobOffer }`. Columns would add mostly-null fields to the hottest table; an
  `EmploymentType` value can't hold the data and clashes with "job type after"; a separate entity would duplicate
  applications, interviews, documents and parsing.
- **Listing company = provider, end employer on the child.** Keeps `CompanyId` required and every existing use
  intact; an unnamed employer is just `EndCompanyId = null`. Alternative (company = employer, nullable) breaks search,
  duplicates and document names.
- **Delete rules.** Listing → TrainingProgram cascades. `EndCompanyId` → Company is `NoAction`, same as the listing's
  company FK, so the user-delete cascade still works in SQLite.
- **Query filter** `t => t.Listing.UserId == CurrentUserId`, same as `ListingSkill`; no `UserId` column.
- **Dates** `DateOnly?` like `PostedOn`; enum stored as string via the existing global convention.
- **Input:** `ListingInput.TrainingProgram` as Hot Chocolate `Optional<TrainingProgramInput?>`: omitted = keep, `null`
  = remove, value = replace. Built-in way to tell "not sent" from "sent null"; avoids the old-client wipe that plain
  "null removes" would cause, and avoids a separate remove mutation. `TrainingProgramInput { EndCompanyName?,
  StartsOn?, EndsOn?, IsPaid?, JobOffer = Unspecified }`.
- **Validation:** `StartsOn > EndsOn` throws `InvalidListingException` (existing typed error).
- **Company lookup:** extract a private `FindOrCreateCompanyAsync` in `ListingMutations.cs` (2nd use), nothing more
  general.
- **Filtering:** `listings(isTrainingProgram: Boolean)` argument, like `archived`/`dueWithinDays`. Hot Chocolate's
  `where` can't test the 1:1 nav for null (its key is non-null, so `eq: null` is rejected), so the planned nested
  filter didn't work.
- **Prompt:** full prompt gets the `trainingProgram` output field and rules; the INTERNSHIP mapping drops
  "Trainee-program"; version +1 (on top of the in-flight title change, so commit that first).

## Risks / Trade-offs

- [Existing program listings saved as normal/INTERNSHIP] → no backfill; owner re-parses or edits the few affected.
- [LLM flags graduate "trainee" jobs as programs] → rule: only when there is a training phase before employment;
  covered by prompt test text.

## Migration Plan

`make migration name=AddTrainingPrograms` (backs up DB). Additive `CREATE TABLE`; rollback = revert migration.
