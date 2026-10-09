# Tasks

## 1. Optional CV on apply

- [x] 1.1 Make `cvDocumentId` nullable in `ApplyToListingAsync` and call `Send` only when given; replace the "CV is required" comment with the why (form-only and lost-CV applications). Verify `dotnet build`
- [x] 1.2 Regenerate `schema.graphql` and verify `SchemaTests` pass with `cvDocumentId: Int`
- [x] 1.3 In `ApplicationTests`, change the `Apply` mutation variable to `Int` and add tests for the spec scenarios "Apply without a CV" and "Undo an application without documents"; verify `dotnet test --filter ApplicationTests` passes

## 2. Docs

- [x] 2.1 Update `docs/STATUS.md`: `applyToListing` input shows `cvDocumentId?`, and the 2026-09-27 decision becomes "documents optional on apply (form-only/lost CVs); sent docs are still marked and re-dated". Verify by reading the diff
- [x] 2.2 Append the why to `.claude/commit-notes.md`
