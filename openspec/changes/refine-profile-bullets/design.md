# Design

## Context

The profile already has three "suggest, review, apply" AI flows that this follows closely:

- `ProfileSkillWording.cs`: prompt query + suggester + `applyProfileSkillWording`, with `ProfileBulletText(id, text)`
  and server-side filtering so the review only shows changes the apply would accept.
- `ProfileTranslationSuggestion.cs`: sends `WritingSample.ForPrompt(...)` samples as raw few-shot examples, the
  established way to copy the owner's voice (STATUS decision 2026-10-15: no distilled style guide).
- `ProfileSummary.cs`: same shape, profile JSON in `<profile>` tags.

The fact guard exists: `TailoredCvWording.KeepsSourceFacts(text, sources, skillNames)` allows new words but rejects
numbers and skill names the sources lack. It backs the base CV `rewrite` mode.

## Goals / Non-Goals

**Goals:**
- One file in `Features/Profiles` mirroring the skill-wording flow, with shared text for built-in and copy-paste.
- Research rules encoded once in the refinement instructions, each traceable to `docs/`.

**Non-Goals:**
- Persisting suggestions or notes. Like the other suggesters, the result lives in the UI until applied.
- Meaning checks beyond numbers and skill names; the owner reviews every suggestion.
- New shared `CvGuidance` constants. The research rule stays local until a second prompt needs it.

## Decisions

### Scope as three optional arguments
`bulletId: Int`, `kind: ProfileEntryKind`, `entryId: Int`, all optional, reusing the existing `ProfileEntryKind`
enum from `addProfileBullet`. A `oneOf` input would be stricter, but this matches `addProfileBullet` and keeps the
UI call simple. The scope is resolved through the user filter first, so a foreign id is "not found" and no data
reaches the AI. Shared by the prompt query, suggest, and preview so all three agree on the targets.

### Send the whole profile's bullets, mark the targets
Every bullet goes into `<profile>` grouped by entry (title, employer/school/project name, project kind, dates,
entry skills), each with `id`, `text`, and `refine: true|false`. The other bullets are the owner's best CV-style
writing sample and let the AI avoid duplicating a sibling. Tokens are cheap at ≤20 bullets per entry. Only targets
are allowed in the reply; the server drops the rest. Alternative: send only the target's entry. Rejected because a
single-bullet refine would lose most of the style signal.

### Style: writing samples + own bullets, no style profile
`WritingSample.ForPrompt(samples in content language, language, 12_000)` → `<writing-samples>`, same budget as
translation and cover letters. Instructions say: copy person, tense, sentence length, punctuation (trailing period
or not, `~30%` style), and recurring verbs and terms from the samples and the bullets marked `refine: false`. Then
`CvGuidance.Voice` and `CvGuidance.Style`. Bullets are written in the profile's `ContentLanguage`. Notes are always
English, because they are UI hints for the owner, not CV text.

### Research check, re-verified against `docs/`
Re-checked against the research docs behind the existing guidance. Each rule the prompt adds, and its source:

| Rule in the prompt | Source in docs |
|---|---|
| A bullet shows what was delivered, the candidate's own part, and the useful result or scope | MASTER §5.3; Tekna "responsibilities, methods and results" (formatting §10, D3) |
| Active language, the candidate's own contribution | NTNU adviser via Kode24 (formatting §10, D1) |
| Concrete work over generic duties or bare technology lists ("Worked with React" → what was built with it) | MASTER §5.3 table; formatting §10 synthesis "evidence of building and solving problems" |
| Keep the team's result as the team's and name the candidate's part | MASTER §5.3; `CvGuidance.HonestClaims` |
| Use a measured result only when the bullet has one; otherwise ask for it in a note | MASTER §5.3 ("valuable when you have one", "do not reuse the fictional samples' … improvements") |
| Technologies in the context they were used | Tekna (formatting §10, D3); MASTER §5.5 |
| No unexplained internal abbreviations or project code names | Tekna (formatting §10, D3) |
| Keep personal, school, and paid work labelled as the source labels it | MASTER §5.5; formatting §10 |
| Do not exaggerate skills or seniority | Kode24 (MASTER §5.3, S7); `CvGuidance.HonestClaims` |
| No stock CV/AI phrasing | `CvGuidance.Style` (Wikipedia "Signs of AI writing") |

What the check found:
- No conflict between the docs and the existing `CvGuidance` rules this reuses.
- `ConciseBullets`' "about 100 characters" is a tailored-layout choice, not research, so it is not applied. The
  prompt only says to cut filler and keep bullets short enough to read in one or two lines; the 500-character
  limit still applies.
- MASTER §12 says the docs can't confirm the owner's real evidence. That is the reason for notes: the AI asks, the
  owner supplies the fact.

### Fact guard per bullet, source = that bullet only
`KeepsSourceFacts(text, [source.Text], userSkillNames)` with the user's whole skill list (`db.Skills`), so any
known technology counts. Siblings are not sources, because that would let a refine merge facts across bullets. A
failing text becomes null and the note is kept, so the owner still sees the question. The rejection itself isn't
reported, the same way tailored bullets fall back silently.

### Reply and result types
Reply: `{"bullets": [{"id": 1, "text": "..."|null, "note": "..."|null}]}` decoded with `JsonReply.TryDecode`.
Result: `ProfileBulletRefinementSuggestion(Status, List<ProfileBulletRefinement>? Bullets, Error, [GraphQLIgnore] ErrorCode)`
with `ProfileBulletRefinement(int BulletId, string? Text, string? Note)`, the same status/error shape as the other
suggesters. One `Filter(reply, targets, sources, skills)` function serves suggest and preview.

### Apply shares the skill-wording save
The bullet loop of `ApplyProfileSkillWordingAsync` moves into an internal helper (for example
`ProfileBulletMutations.ReplaceTextsAsync`), used by both apply mutations. Not fact-checked on apply: the owner
fills the gaps the notes asked about. Alternative: reuse `applyProfileSkillWording` with null headline/summary.
Rejected because the name would mislead the UI and its future rules.

### LLM call
`AiOperations.RunAsync("refine-profile-bullets", ...)`, `LlmExecution.RunJsonAsync`, JSON response format,
temperature 0.3 (as translation: some freedom to reword), `MaxOutputTokens` 32 768, prompt `Version = 1`.

## Risks / Trade-offs

- [Guard only knows the skill list] A technology missing from the user's skills slips past the check. → The prompt
  forbids new technologies; the owner reviews every suggestion.
- [Rewording can shift meaning, e.g. "helped" → "built"] Not machine-checkable. → `HonestClaims` in the prompt;
  the UI shows the source next to each suggestion.
- [Whole-profile run on a large profile] A long reply can be cut off. → 32k output cap; entry and single-bullet
  scopes are the fallback.
- [Style drift with no samples] With no writing samples, only the bullets show the style. → Acceptable; samples are
  optional everywhere else too.
