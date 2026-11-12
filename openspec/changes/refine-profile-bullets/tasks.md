# Tasks

## 1. Shared bullet save

- [x] 1.1 Move the bullet loop of `ApplyProfileSkillWordingAsync` into an internal helper in `ProfileBulletMutations`
  (all ids checked before any change, duplicate → `InvalidProfileException`) and call it from skill wording; verify
  with `dotnet test --filter ProfileSkillWordingTests`

## 2. Prompt and data

- [x] 2.1 Add `Features/Profiles/ProfileBulletRefinement.cs` with scope resolution (`bulletId` | `kind`+`entryId` |
  none → target ids, via the user filter; invalid combos, empty scope → `InvalidProfileException`; unknown/foreign →
  `ProfileEntryNotFoundException`), and add tests for each scope and error case (spec: Refinement scope, Invalid scope)
- [x] 2.2 Build `ContentAsync`: `<writing-samples>` from `WritingSample.ForPrompt` in the content language (12 000
  budget) and `<profile>` JSON of entries with title/name, kind, dates, entry skills and bullets `{id, text, refine}`,
  with no personal details; test samples-by-language, other bullets marked `refine: false`, and no email, phone or
  address in the prompt
- [x] 2.3 Write `Instructions(language, external)` with the research rules from design.md, the note rule (English,
  ≤300 chars, ask instead of invent), style copying, `CvGuidance.Voice`/`Style`/`HonestClaims`, the JSON shape and
  `JsonReply.Rule`; add the `profileBulletRefinementPrompt` query (`Version = 1`, scope errors as `GraphQLException`)
  and test that it contains the instructions, the targets, and the note rule

## 3. Suggest and preview

- [x] 3.1 Add the result records and one `Filter` that keeps targets only, cleans text and note, nulls text that
  fails `KeepsSourceFacts([source], userSkills)`, is >500 chars or equals the source, drops notes >300, and omits empty
  suggestions; unit-test the spec's fact-guard scenarios (added metric, added skill, sibling skill, reworded facts kept)
- [x] 3.2 Add `ProfileBulletRefiner` (`refine-profile-bullets` via `AiOperations` + `LlmExecution.RunJsonAsync`),
  register it in `Program.cs`, and the `refineProfileBullets` mutation with `model`/`requestId`; test with
  `factory.Llm`: success, unreadable reply → FAILED, usage row logged, no bullet changed, unknown model error, guest
  can't reach owner bullets
- [x] 3.3 Add `previewProfileBulletRefinement(json, scope)` using the same decode and `Filter`; test pasted-reply guard
  and invalid JSON → FAILED

## 4. Apply

- [x] 4.1 Add `applyProfileBulletRefinement(bullets)` using the helper from 1.1, returning the profile; test apply two
  bullets (same ids/positions), user-added metric saved, foreign id → nothing changes, duplicate id, and a translated
  bullet reports stale afterwards

## 5. Contract and docs

- [x] 5.1 Run `dotnet test` (full suite) so `SchemaTests` refreshes `schema.graphql`, then check the diff shows only the
  new query, mutations and types
- [x] 5.2 Update the Profile line in `docs/STATUS.md` via `log-progress` (scopes, fact guard, notes, style source) and
  add a `.claude/commit-notes.md` line for the change
