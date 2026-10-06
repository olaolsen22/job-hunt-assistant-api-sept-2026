# Tasks

## 1. Parse timeout

- [x] 1.1 In `ListingParser.RunAsync` catch `TaskCanceledException` when the request token isn't cancelled: job FAILED with "The AI took too long to answer. Try again.", usage `ErrorCode = "timeout"`; verify a new `ParseTests` case (fake client throws `TaskCanceledException(new TimeoutException())`) asserts FAILED, the error text, a saved ParseJob readable via `parseJob(id)`, and a usage row with `timeout`
- [x] 1.2 Add `timeout` to the `LlmUsage` error codes in `docs/STATUS.md` and append the why to `.claude/commit-notes.md`; verify both mention it

## 2. Docs

- [x] 2.1 Rewrite `docs/PLAN.md` §4 data model to match the code and the planned changes (listing as one attempt, status + timeline on listing, documents per listing, final statuses and "closed", DATE/ASAP/UNSPECIFIED); verify no `Application` entity or `ROLLING` remains in §4
- [x] 2.2 Rewrite `docs/PLAN.md` §7 API surface with the real query/mutation names (`listings`, `applyToListing`, `recordOutcome`, `parseListing`, ...) and keep the planned-but-unbuilt items marked as planned; verify every built name matches `schema.graphql`
- [x] 2.3 Fix `docs/PLAN.md` §9: M3 CVs relational (no JSON columns), CV generation creates a document on a listing, M2 notes/contacts still pending; verify §4 and §9 no longer contradict
- [x] 2.4 Add a "Deferred follow-ups" list to `docs/PLAN.md`: guest AI cap before deploy, user scoping for background work, store identical CV files once; verify each item names the milestone it belongs to
- [x] 2.5 Fix the `docs/STATUS.md` header to the real last logged commit and append the why to `.claude/commit-notes.md`; verify the hash exists in `git log`

## 3. Integration check

- [x] 3.1 Run the full `dotnet test` and verify all tests pass
