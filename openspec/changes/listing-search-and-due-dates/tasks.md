# Tasks

## 1. Merge ROLLING into ASAP

- [x] 1.1 Remove `Rolling` from `DeadlineType`; in both parse prompts map "Snarest"/"ASAP"/"straks"/"Løpende"/"fortløpende"/"rolling" to ASAP and bump `Version` to 4 and `QuickVersion` to 2; verify `ListingParsePromptTests` updated to assert no `"ROLLING"`, the ASAP mapping and versions 4 and 2 pass
- [x] 1.2 Add migration `MergeRollingDeadlines` with `make migration name=MergeRollingDeadlines` and the `Rolling` → `Asap` `UPDATE`; verify the migration's `Up` contains the `UPDATE` and `dotnet test` passes
- [x] 1.3 Regenerate `schema.graphql`; verify `SchemaTests` passes and `DeadlineType` lists only DATE, ASAP, UNSPECIFIED
- [x] 1.4 Update `docs/STATUS.md` (DeadlineType, prompt versions, BREAKING note for the UI, stored ROLLING parse results) with `/log-progress` and append the why to `.claude/commit-notes.md`; verify both mention the merge

## 2. Effective due date

- [x] 2.1 Add the due-date expression (DATE → deadlineOn, ASAP → posted + 14, UNSPECIFIED → posted + 30) and use it in the default order; verify the updated `Listings_DefaultOrder_ClosestDeadlineFirst` expects `soon date, recent asap, undated, later date, stale asap, expired date` and passes
- [x] 2.2 Add `Listing.dueOn` via a resolver on `ListingNode` using the same expression, with `deadlineType`/`postedOn`/`deadlineOn` projected; verify a new test asserts `dueOn` for ASAP, UNSPECIFIED and DATE listings, selecting only `dueOn`
- [x] 2.3 Add `listings(dueWithinDays: Int)` (today ≤ due ≤ today + N, negative → GraphQL error); verify new tests: due today and in 7 days returned, in 8 days and yesterday not, `-1` returns an error
- [x] 2.4 Verify `postedOn` and `deadlineOn` `gte`/`lte` ranges with new tests: posted range alone, deadline range excludes ASAP/UNSPECIFIED, both together
- [x] 2.5 Regenerate `schema.graphql`, update `docs/STATUS.md` (due rule, `dueOn`, `dueWithinDays`, order) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS describes the rule

## 3. Text search

- [x] 3.1 Add `NormalizedTitle` to `Listing`, set it wherever `Title` is set in `ListingMutations`, and add migration `AddListingNormalizedTitle` with the æøå backfill `UPDATE`; verify `dotnet test` passes and the migration contains the backfill `UPDATE`
- [x] 3.2 Add `listings(search: String)` matching `NormalizedTitle`, company and job role `NormalizedName`; blank skips the filter; verify new tests: "ØKONOMI" and "økonomi" both find "Utvikler på Økonomi", company and role matches, description-only word not found, search combined with a status `where`
- [x] 3.3 Regenerate `schema.graphql`, update `docs/STATUS.md` (search, migration list, non-ASCII backfill limit) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes

## 4. Integration check

- [x] 4.1 Run the full `dotnet test` and verify all tests pass
