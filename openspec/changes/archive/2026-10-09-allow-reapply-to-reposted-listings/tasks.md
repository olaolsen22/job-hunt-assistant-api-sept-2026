# Tasks

## 1. ACCEPTED and offer withdrawal

- [x] 1.1 Add `Accepted` to `ApplicationStatus` and `ApplicationOutcome`, and a single final-status set (REJECTED, WITHDRAWN, ACCEPTED) next to the enum; verify `dotnet build` passes
- [x] 1.2 Add OFFER → ACCEPTED and OFFER → REJECTED to the transition table and make the `recordOutcome` mapping explicit (no `_ =>` default); verify new `ApplicationTests` cases pass: accept an offer, company withdraws an offer, accept from APPLIED/INTERVIEWING fails, outcome after ACCEPTED fails, undo ACCEPTED returns to OFFER
- [x] 1.3 Verify `addInterview` on an ACCEPTED listing fails with `InvalidStatusTransitionError` (new `InterviewTests` case)
- [x] 1.4 Regenerate `schema.graphql` and verify `SchemaTests` passes and the SDL shows `ACCEPTED` in `ApplicationStatus` and `ApplicationOutcome`
- [x] 1.5 Update `docs/STATUS.md` transitions line with `/log-progress`, append the why to `.claude/commit-notes.md`, and verify both files mention ACCEPTED

## 2. One open listing per URL

- [x] 2.1 Change the (UserId, Url) unique index to a partial index filtered on non-final statuses (`HasFilter`) and add a migration with `make migration name=AllowReapplyToListings`; verify the migration's `Up` drops and recreates the index with the filter and `dotnet test` still passes
- [x] 2.2 Make the duplicate check in `ListingMutations.ApplyAsync` ignore closed listings and skip the check when the listing being saved is closed; verify `ListingTests`: creating fails while the other listing is SAVED (incl. archived), APPLIED, INTERVIEWING or OFFER, and succeeds after REJECTED, WITHDRAWN and ACCEPTED
- [x] 2.3 Verify the update cases in `ListingTests`: open listing to an open listing's URL fails; closed listing to an open listing's URL succeeds
- [x] 2.4 Add `previousAttempts` to `CreateListingPayload` (closed listings with the same URL, newest first) and exclude same-URL listings from `possibleDuplicates`; verify `ListingTests`: previous attempt returned, empty list on a first attempt, previous attempt not listed as a possible duplicate
- [x] 2.5 Block `undoStatusChange` with `DuplicateListingError` when it would reopen a listing while another open listing has the same URL (add the error to the mutation); verify `ApplicationTests`: undo blocked by a newer SAVED attempt, allowed when it's the only listing, allowed when the newer attempt is closed too
- [x] 2.6 Regenerate `schema.graphql`; verify `SchemaTests` passes and the SDL shows `previousAttempts` and `DuplicateListingError` on the undo payload
- [x] 2.7 Update `docs/STATUS.md` (duplicate rule, previous attempts, migration list, decisions log) with `/log-progress`, append the why to `.claude/commit-notes.md`, and verify both reflect the open-URL rule

## 3. Integration check

- [x] 3.1 Run the full `dotnet test` and verify all tests pass, including the existing archived-duplicate test unchanged
