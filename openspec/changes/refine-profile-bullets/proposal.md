# Proposal

## Why

Every CV the app builds starts from the profile's bullets, but nothing helps make those bullets better. Tailoring
can only select, shorten, or add listing terms, so a weak source bullet such as "Worked with React" stays weak in
every CV. The research in `docs/` (what you delivered + your own part + the result, active language, no inflation)
is only applied per listing, not to the master text. Improving a bullet by hand is slow, and generic AI rewrites
sound like stock CV text rather than the owner.

## What Changes

- New AI suggestion that refines profile bullets in one of three scopes: every bullet, one entry's bullets, or a
  single bullet.
- Each bullet is checked against the bullet research the project already relies on. The AI may reword and tighten a
  bullet but keeps its facts. When a bullet lacks something only the owner knows (their own part, the result, the
  scale, what was built), it returns a short note asking for it instead of inventing it.
- Suggestions copy the owner's writing style: writing samples in the profile's content language and the profile's
  own bullets are sent as examples, together with the shared voice and anti-AI-phrasing rules.
- The server holds each suggested text to its source bullet's facts: a suggestion that adds a number, or a skill
  name the source bullet doesn't mention, is dropped, and only its note (if any) is kept. Suggestions that change
  nothing are left out.
- Copy-paste path: a prompt query builds the same instructions and data for an external AI, and a preview mutation
  runs the pasted reply through the same checks.
- Applying accepted suggestions saves the reviewed texts in one save. Ids and positions stay the same. Existing
  translations of a changed bullet become stale, as they do after any edit.
- Out of scope: headline and summary (summary already has `suggestProfileSummary`), splitting, merging or deleting
  bullets, and listing-specific tailoring.

## Capabilities

### New Capabilities

- `profile-bullet-refinement`: AI-suggested improvements to profile bullets by scope (all, entry, one), checked
  against the CV research, written in the owner's style, held to each source bullet's facts, with notes for missing
  facts, and applied after review.

### Modified Capabilities

_None._ Bullet storage, validation and translation staleness keep their current rules.

## Impact

- New: `Features/Profiles/ProfileBulletRefinement.cs` (prompt query, suggester, suggest/preview/apply mutations),
  registered in `Program.cs`.
- Reused: `WritingSample.ForPrompt`, `CvGuidance.Voice`/`Style`/`HonestClaims`,
  `TailoredCvWording.KeepsSourceFacts`, `AiOperations`, `LlmExecution`, `JsonReply`.
- Small refactor: the bullet-saving loop of `applyProfileSkillWording` is shared with the new apply mutation.
- GraphQL: new query `profileBulletRefinementPrompt`, mutations `refineProfileBullets`,
  `previewProfileBulletRefinement`, `applyProfileBulletRefinement`. No schema change to existing types.
- No migration; nothing new is stored. LLM usage is logged under a new operation name.
- Docs: `docs/STATUS.md` profile rules.
