# Tasks

## 1. Reproduce

- [x] 1.1 Add a batch test in `ListingTests` that sends two `createListing` aliases in one request with the same title and role at different companies; verify whether it fails and record which suspect from design.md it hits

## 2. Fix

- [x] 2.1 Fix the cause in `ListingMutations.CreateListingAsync` (or `ApplyAsync` if the company resolves wrongly); verify the batch test passes (no API bug: batch test passes; closed without a fix)
- [x] 2.2 Add the existing-company variant (each row at an already saved, different company); verify `possibleDuplicates` is empty for both rows
- [x] 2.3 Verify the existing same-company test still returns same-role and same-title matches; run `dotnet test --filter ListingTests`

## 3. Wrap up

- [x] 3.1 Update the duplicate rule in `docs/STATUS.md` with `/log-progress` and append the why to `.claude/commit-notes.md`; verify both mention the batch case
