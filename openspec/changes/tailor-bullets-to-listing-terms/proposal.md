# Proposal

## Why

Tailored CV bullets almost never pick up the listing's wording. Detailed copies each bullet word for word.
Concise is allowed to use listing terms, but its prompt says "cut words, not rewriting", so the model plays safe.
For example, a listing asks for "working within cross-functional teams" and the profile bullet "Streamlined
development process from start to finish with product, design, and mobile teams, making it ~30% faster with a more
consistent UI." clearly shows that work, but the bullet comes back unchanged. That costs ATS and recruiter matches
the profile has actually earned.

## What Changes

- Both templates may reword a selected bullet to use the listing's terms wherever the bullet's facts support them
  (for example "with product, design, and mobile teams" → "within cross-functional product, design, and mobile
  teams"). There is no per-bullet cap, but bullets must read naturally: the AI must not copy listing sentences,
  stack buzzwords, or force a term onto a bullet that doesn't show it.
- **Detailed**: bullets are no longer copied word for word in a listing-tailored CV. They keep their full length
  and facts, and the same listing-aware word check as Concise (source words, listing words, and at most 2 other
  words, with no number the source doesn't state) holds them to the source. A bullet that fails the check falls
  back to its source text.
- **Concise**: shortening may also swap in listing terms and reorder words, instead of only cutting.
- **External tailored-CV preview**: a Detailed candidate bullet whose text differs from its source is no longer
  rejected. It is checked like Concise and falls back to the source text when it fails. The external prompt
  version goes up.
- Out of scope: base CVs (they have no listing, so they keep exact/shorten/rewrite as they work now), the
  plain-language rewrite mode, paragraphs, and summaries.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `tailored-cv-drafts`: source-attributed bullets in listing-tailored analysis and external previews may use the
  listing's wording, held to the source's words and numbers, instead of having to match the source text exactly.

## Impact

- Prompts: `Prompts/CvGuidance.cs` (new shared listing-wording rule; Concise shortening rule moves its listing
  sentence there so base CVs don't get it).
- Built-in analysis: `TailoredCvAnalysisInstructions.cs` (Detailed returns tailored bullet text),
  `TailoredCvAnalysisDecoder.cs` (one bullet decoder for both templates).
- External flow: `TailoredCvExternalPromptQueries.cs` (Detailed bullet rule, version bump),
  `TailoredCvExternalPreviewMutations.cs` (no exact-text requirement for tailored previews).
- Wording check: `TailoredCvWording.cs` (`ModeFor` distinguishes listing-tailored CVs from base CVs).
- GraphQL schema: no shape change. `TailoredCvAnalysisResult.bullets` already carries the text; for Detailed it can
  now differ from the profile bullet.
- Builds on the uncommitted CV-prompt work already in these files.
