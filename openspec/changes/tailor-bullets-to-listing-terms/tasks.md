# Tasks

## 1. Wording mode

- [x] 1.1 In `TailoredCvWording.cs`, rename `TailoredCvWordingMode.Shorten` to `SourceWords`. Add a `listing` flag to `ModeFor`: Concise or `listing` gives `SourceWords`, `rewrite` still wins, base CV callers stay unchanged. Update comments. Verify that `dotnet build` passes and `dotnet test --filter BaseCvExternalPrompt` stays green.
- [x] 1.2 Add a `KeepsSourceWords` test for the owner's example: source "Streamlined development process from start to finish with product, design, and mobile teams, making it ~30% faster with a more consistent UI.", tailored text "... within cross-functional product, design, and mobile teams ...", and a listing containing "Working within cross-functional teams". It passes. A variant with a listing-only number ("40%") fails. Verify with `dotnet test --filter TailoredCvTemplate`.

## 2. Prompt rules

- [x] 2.1 In `CvGuidance.cs`, add `ListingWording` (design decision 4). Remove the listing sentence from `ConciseBullets` and relax its "not rewriting / keep word order" wording so listing-term swaps are allowed. Verify that the base CV prompt tests still pass and that the base CV prompt has no "listing's exact phrase".

## 3. Built-in analysis

- [x] 3.1 In `TailoredCvAnalysisInstructions.EntryInstructions`, make the Detailed prompt ask for `bullets:[{id,text}]` with tailored, full-length text plus `ListingWording`. Concise appends `ListingWording` after `ConciseBullets`. Verify that the instructions contain the new rule for both templates.
- [x] 3.2 In `TailoredCvAnalysisDecoder`, decode bullet text for both templates (`DecodeBullets`), with `listingText` for both and fallback to the source on a failing, blank, or too-long text. Leave the Detailed paragraph handling as it is. Update `TailoredCvTemplateTests` for the Detailed analysis: a tailored listing-term bullet is kept, and one with an invented number or too many new words falls back. Verify with `dotnet test --filter TailoredCvTemplate`.

## 4. External prompt and preview

- [x] 4.1 In `TailoredCvExternalPromptQueries`, replace the Detailed "preserve the exact original bullet text" rule with the tailored rule plus `ListingWording`, use the same bullet-language line as Concise, and bump `Version` to 19. Update the test that asserts the exact-text phrase. Verify with `dotnet test --filter TailoredCvTemplate`.
- [x] 4.2 In `TailoredCvExternalPreviewMutations`, use `ModeFor(template, listing: true)` so Detailed previews skip the exact-text requirement and run `KeepSourceWordingAsync`. Add preview tests for Detailed: tailored text is kept, altered text falls back to the source (no error), and a bullet from another user's profile is still rejected. Verify with `dotnet test --filter TailoredCvTemplate`.

## 5. Docs and integration

- [x] 5.1 Update the bullet-wording section of `docs/tailored-cv-drafts.md`, and run `log-progress` to update the relevant `docs/STATUS.md` lines. Verify that no unrelated sections changed.
- [x] 5.2 Add a commit note to `.claude/commit-notes.md`, run the full `dotnet test`, and confirm `schema.graphql` has no shape change.
