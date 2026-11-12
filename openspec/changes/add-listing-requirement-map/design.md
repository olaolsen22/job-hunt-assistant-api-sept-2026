# Design

## Context

See proposal.md for motivation. Relevant current state:

- Cover letters already implement the exact flow this change needs, in one slice: a 1:1-per-listing entity with a JSON
  content column (`CoverLetterDraft`), a shared prompt context for both AI paths (`CoverLetterContext`), a generator
  running through `AiOperations` + `LlmExecution.RunJsonAsync`, an external-prompt query, a preview mutation and an
  explicit save. The requirement map copies that shape.
- `Listing.AnalyzedAt` / `ReanalyzedAt` already mean "parsed by AI". The new concept therefore avoids the word
  "analysis" in names: it is a requirement map.
- The listing stores structured requirements (`ListingSkill` with level, min years, required; `ListingLanguage`;
  seniority; work mode). Duties and themes exist only in the free-text description.
- Profile bullets share one id space across experiences, educations, certifications and projects, and ids survive
  edits and moves.
- `cleanUpSkills` rewrites skill ids stored inside draft and base CV JSON.

## Goals / Non-Goals

**Goals:**

- One stored, user-corrected statement of what a listing needs and what the profile shows for it.
- Same two AI paths and review-then-save behavior as the rest of the app.
- A small slice that later changes can read without it knowing about them.

**Non-Goals:**

- No consumer wiring: CV, base CV and cover letter prompts are untouched (no prompt version bumps).
- No deterministic matching (skill ids, years vs `MinYears`) and no fit score.
- No relational querying of needs (dashboards, filters by gap).
- No guest seed data for maps.

## Decisions

**1. New slice `Features/RequirementMaps/`, not part of `Listings` or `TailoredCvDrafts`.**
It reads both the listing and the profile and is consumed by several features later, so it belongs to none of them.
Files: `RequirementMap.cs` (entity, content records, EF configuration, GraphQL node), `RequirementMapPrompt.cs`
(context, instructions, version, decode), `RequirementMapGenerator.cs`, `RequirementMapMutations.cs` (queries,
mutations, validation). Alternative: fields on `Listing`; rejected because `updateListing` is a full replace and would
have to carry or protect the map.

**2. One row per listing with a JSON content column.**
`RequirementMap { Id, UserId, ListingId (unique), ContentJson, CreatedAt, UpdatedAt }`, cascade from user and listing,
`UserId` index, global user filter. The UI edits the map as a whole and nothing queries inside it, which is the
project's rule for JSON leaf data and the same choice as CV and cover letter drafts. Alternative: a `ListingNeed` table
with evidence join rows; rejected for now because no query needs it, and evidence would need four nullable FKs or a
polymorphic key. If dashboards later need gaps, a table can be introduced then.

**3. Evidence is bullet ids plus entry references; no skill ids.**
`Need { Text, Kind, Importance, Match, BulletIds, Entries[{Kind, Id}], Note }`. Entry references cover needs that no
bullet states (a degree, a skill tagged on a job). Profile skill ids are left out on purpose: storing them would add
this table to the `cleanUpSkills` id-rewrite path. A skill need is evidenced by the entries and bullets where the
skill was used, which is also what a CV has to show. Languages and practical needs (location, work mode) have no row
to point at, so a note is accepted as their support.

**4. References are a snapshot.**
Validated against the profile when saved; never rewritten or cascaded afterwards. Consumers (later changes) ignore ids
that no longer exist. This matches how drafts treat source ids and avoids FKs out of a JSON column.

**5. Strict on save, lenient on AI candidates.**
User-submitted saves are rejected on any invalid reference or limit (input validation). Model output is normalized
instead: unknown ids dropped, unsupported EVIDENCED/PARTIAL returned as GAP, overflow trimmed. One invented id should
not cost the whole map, and the candidate is shown for review before it can be saved. Downgrading to GAP is the safe
direction: it can only understate fit. A candidate with no needs after decoding is a failure (`invalid-json` on the
built-in path, `InvalidRequirementMapError` on preview).

**6. One LLM call per map.**
Listing plus the whole allowlisted profile in one request, JSON response, low temperature (0.2) since this is
classification rather than writing. Context capped like the cover letter's (`MaxContextLength`), failing with
`InvalidRequirementMapError` beyond it. Operation name `requirement-map` for usage logging; AI operation key
`generate-requirement-map`.

**7. Prompt context built in this slice from an explicit allowlist.**
Listing: kind, title, company, job role, url (external prompt only), description, seniority, employment type, work
mode, locations, skills (name, level, min years, required), languages; company research kind and description.
Profile: experiences, educations, certifications and projects with ids, dates, titles, tagged skill names and bullets
(id + text); profile skills (name, level); languages (name, written, spoken); `experienceMonths`; home city, target
cities and work modes. Nothing else. `TailoredCvListingContext` is reused for the listing part where it already fits;
the profile part is written here rather than widening the CV prompt's helper, which the next change will rework.

**8. Text languages.**
Need text is written in the listing's language (it paraphrases the listing). Notes are in English, as bullet
refinement notes already are.

**9. GraphQL surface.**
Queries: `requirementMap(listingId)`, `requirementMapExternalPrompt(listingId)` (bad input reported as GraphQL errors,
per convention). Mutations: `generateRequirementMap(listingId, model, requestId)` → `result`,
`previewRequirementMap(listingId, json)` → `candidate`, `saveRequirementMap(listingId, content)`,
`deleteRequirementMap(listingId)`. Errors: `ListingNotFoundError`, `InvalidRequirementMapError`,
`RequirementMapNotFoundError`, `UnknownLlmModelError`, `AiSafetyError`.

## Risks / Trade-offs

- [Map goes stale after the listing is re-parsed or the profile grows] → `updatedAt` is exposed so the UI can compare
  it with the listing's; regenerating is one click. No automatic invalidation.
- [The model overstates fit] → mapping rules in the prompt, server downgrade of unsupported matches, and mandatory
  review before save. Meaning still cannot be checked by code.
- [No consumer yet, so the map is only a fit view until the next change] → accepted: it is useful alone for deciding
  effort, and shipping it first keeps the synthesis rework smaller.
- [JSON column blocks later gap statistics] → accepted; see decision 2.
- [Large profiles exceed the context cap] → clear error naming the limit, same behavior as cover letters.

## Migration Plan

Additive: one new table `RequirementMaps`. Run `make backup`, then `make migration name=AddRequirementMaps`, and review
`Up()` to confirm it only creates the table and its indexes. `Down` drops the table (saved maps are lost; restore a
backup instead).
