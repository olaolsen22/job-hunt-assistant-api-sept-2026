# Tasks

## 1. Full listing prompt

- [x] 1.1 In `GetListingParsePromptAsync`, replace "Use null when the ad doesn't say. Don't guess." with the
  infer-from-signals / never-invent rule. Add rules for title (Stillingstittel), the seniority cutoffs (replacing
  "Not from years alone"), workMode, relative deadlines ("i dag"/"i morgen"), postedOn (not "Sist endret") and
  skills-only-from-the-posting. Verify `dotnet build` passes
- [x] 1.2 Bump `Version` 4 → 5. Update `ListingParsePrompt_IncludesEnumValuesAndOwnNames` to assert version 5 and the
  key phrases from the spec scenarios ("Stillingstittel", "erfaren", "hjemmekontor", "Sist endret", "i dag", no
  "Don't guess", no "Not from years alone"). Verify with
  `dotnet test --filter ListingParsePromptTests`

## 2. Quick listing prompt

- [x] 2.1 In `GetListingQuickParsePromptAsync`, apply the same infer/never-invent line, title, postedOn and
  relative-deadline rules (no seniority/workMode/skills). Bump `QuickVersion` 2 → 3. Update
  `ListingQuickParsePrompt_AsksForArrayWithOwnRoles` to assert version 3, "Stillingstittel" and "i dag". Verify
  with `dotnet test --filter ListingParsePromptTests`

## 3. Docs and integration

- [x] 3.1 Update the prompt versions in `docs/STATUS.md` (Version = 5, QuickVersion = 3) and add a line to
  `.claude/commit-notes.md`. Verify the full `dotnet test` suite passes
