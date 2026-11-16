# Tasks

## 1. Entity and storage

- [x] 1.1 Add `Features/RequirementMaps/RequirementMap.cs`: entity (Id, UserId, ListingId, ContentJson, CreatedAt, UpdatedAt), content records (`RequirementMapContent`, `RequirementNeed`, `RequirementEntryRef`) and enums (kind, importance, match, entry kind), EF configuration (unique ListingId, UserId index, cascade from user and listing), and a GraphQL node hiding UserId/ListingId/ContentJson; verify `dotnet build` passes
- [x] 1.2 Register the set and `UserId == CurrentUserId` filter in `AppDbContext`; verify `dotnet test --filter QueryFilterTests` passes
- [x] 1.3 Run `make backup`, then `make migration name=AddRequirementMaps`; verify `Up()` only creates the `RequirementMaps` table and its indexes

## 2. Save, read, delete

- [x] 2.1 Add `RequirementMapMutations.cs` with the `requirementMap(listingId)` query, `saveRequirementMap` (limits, own-profile evidence check, GAP clears evidence, EVIDENCED/PARTIAL needs a reference or note, full replace keeping CreatedAt) and `deleteRequirementMap`, with `InvalidRequirementMapException` and `RequirementMapNotFoundException`; verify `dotnet build` passes
- [x] 2.2 Add `tests/JobHunt.Api.Tests/Features/RequirementMaps/RequirementMapTests.cs` covering the spec scenarios for content limits, evidence ownership, match/evidence agreement, replace on second save, foreign listing, null reads, snapshot after a bullet is deleted, delete and listing-delete cascade; verify `dotnet test --filter RequirementMapTests` passes

## 3. Prompt and AI candidates

- [x] 3.1 Add `RequirementMapPrompt.cs`: `Version`, allowlisted context builder (listing via `TailoredCvListingContext` where it fits, profile part per design decision 7, context length cap, refusal when the listing has no description and no skills), instructions with the mapping rules, and `Decode` that normalizes candidates (drop unknown ids, downgrade unsupported matches to GAP, trim overflow, fail on no needs); verify `dotnet build` passes
- [x] 3.2 Add `requirementMapExternalPrompt(listingId)` and `previewRequirementMap(listingId, json)`; add tests for prompt content (rules present, sensitive values absent, url present), valid paste, malformed paste, invented and partly valid evidence ids; verify `dotnet test --filter RequirementMapTests` passes
- [x] 3.3 Add `RequirementMapGenerator.cs` (through `AiOperations` and `LlmExecution.RunJsonAsync`, operation `requirement-map`), register it in `Program.cs`, and add `generateRequirementMap(listingId, model, requestId)`; add tests using `factory.Llm` for success (candidate returned, usage row logged, saved map unchanged), unreadable answer, unknown model and the empty-listing refusal; verify `dotnet test --filter RequirementMapTests` passes

## 4. Contract and docs

- [x] 4.1 Run `dotnet test --filter SchemaTests` twice so `schema.graphql` is rewritten and then passes; verify the diff only adds the two queries, four mutations and their types
- [x] 4.2 Write `docs/requirement-map.md` as a short UI integration guide (queries and mutations with example documents, the review-then-save flow, validation rules the UI must expect, snapshot references); verify each example document matches `schema.graphql`
- [x] 4.3 Update `docs/STATUS.md` with the `log-progress` skill (feature rule line, decision log entry, lossy `Down` note, and "Next up" pointing at the synthesis rework that will read the map) and add the rationale line to `.local/commit-notes.md`; verify both files mention the requirement map

## 5. Integration check

- [x] 5.1 Run the full `dotnet test` once; verify every test passes
