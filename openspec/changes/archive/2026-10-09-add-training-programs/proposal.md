# Proposal

## Why

Some ads are not jobs but training programs that lead to one: an academy (e.g. AW Academy) runs an often unpaid
bootcamp and places graduates with an employer (e.g. Kongsberg Defence Communication), sometimes guaranteed on
passing an exam, sometimes vaguely "with partner companies". Today these get saved as normal listings, the parser
guesses INTERNSHIP, and the provider/employer split, program dates and job promise are lost, so the listing doesn't
show that it's a program.

## What Changes

- New optional 1:1 `TrainingProgram` on a listing: end employer (optional company), start and end dates, whether the
  program is paid (unknown allowed), and what job it promises (guaranteed, on passing, none, unspecified).
- A listing's `company` is the program provider whenever a training program is attached, so it's always known; the end
  employer may be unnamed.
- `ListingInput` gets an optional nested `trainingProgram`: omitted keeps it, `null` removes it, a value sets it.
- `Listing.trainingProgram` is exposed in GraphQL and filterable, so the UI can badge and filter programs.
- Full listing parse prompt extracts `trainingProgram`, treats `companyName` as the provider for programs, and no
  longer maps training programs to INTERNSHIP. Prompt version bumped.

## Capabilities

### New Capabilities
- `training-programs`: storing, editing, removing and querying the training program attached to a listing.

### Modified Capabilities
- `listing-parsing`: the full prompt extracts training programs and stops classifying them as internships.

## Impact

- Entities/DB: new `TrainingPrograms` table (additive migration, Listings table unchanged).
- `Features/Listings`: `Listing.cs`, `ListingMutations.cs` (input, apply, include), `ListingParsePromptQueries.cs`.
- `Data/AppDbContext.cs`: query filter for the new entity.
- `schema.graphql` regenerated; UI repo can adopt `trainingProgram` when it wants (old clients unaffected).
