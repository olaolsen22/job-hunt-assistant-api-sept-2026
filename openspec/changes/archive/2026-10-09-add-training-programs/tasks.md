# Tasks

## 0. Prerequisite

- [x] 0.1 Commit the in-flight short-English-title prompt change first, so this change's version bump is its own commit (verify: `git status` clean for `ListingParsePromptQueries.cs`).

## 1. Entity and migration

- [x] 1.1 Add `TrainingProgram` entity + `JobOfferType` enum in `Features/Listings/`, `Listing.TrainingProgram?` nav, config (PK = ListingId, cascade from Listing, `NoAction` to EndCompany) and query filter `t.Listing.UserId == CurrentUserId` in `AppDbContext` (verify: `dotnet build`).
- [x] 1.2 `make migration name=AddTrainingPrograms`; check the migration only creates `TrainingPrograms` (verify: migration file has CREATE TABLE only, no Listings rebuild).

## 2. Mutations and queries

- [x] 2.1 Extract private `FindOrCreateCompanyAsync` in `ListingMutations.cs` and use it for the listing company (verify: existing `ListingTests` pass).
- [x] 2.2 Add `TrainingProgramInput` and `Optional<TrainingProgramInput?> TrainingProgram` on `ListingInput`; in `ApplyAsync` keep/remove/replace, resolve end company, throw `InvalidListingException` when startsOn > endsOn; include the nav in the details query (verify: tests in 2.3).
- [x] 2.3 Tests in `ListingTests` (or new `ListingTrainingProgramTests`): create with program, unnamed end employer, existing end company reused, omitted keeps, null removes, invalid dates error, other user can't see, listing delete removes program (verify: `dotnet test --filter TrainingProgram`).
- [x] 2.4 Filter test: listings filtered to those with a program; via the `isTrainingProgram` argument (nested `where` can't null-test the nav) (verify: test passes).

## 3. Parse prompt

- [x] 3.1 Full prompt: add `trainingProgram` output field and rules (provider = companyName, endCompanyName null for vague partners, isPaid null unless stated, only for training-then-job ads), remove "Trainee-program" from the INTERNSHIP mapping, bump `Version` by one (verify: `ListingParsePromptTests` updated for new version and text).
- [x] 3.2 Test the quick prompt is unchanged in version and has no `trainingProgram` (verify: `dotnet test --filter ListingParsePrompt`).

## 4. Docs

- [x] 4.1 Regenerate `schema.graphql`, run full `dotnet test`, and `/log-progress` to update `docs/STATUS.md` (verify: schema contains `TrainingProgram`, all tests green).
