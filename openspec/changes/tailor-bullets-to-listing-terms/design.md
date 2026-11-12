# Design

## Context

`TailoredCvWording.ModeFor(template, rewrite)` picks one of three wording modes, and base CVs and listing-tailored
CVs share it:

- `Exact` (Detailed): built-in analysis returns only `selectedBulletIds`, and the decoder copies the source text.
  The external preview passes `requireSourceBulletText: true`, so any difference is a validation error.
- `Shorten` (Concise): the model returns bullet text. `KeepsSourceWords(text, sources, listingText)` accepts it when
  every number is in the source and at most `MaxNewWords` (2) words are in neither the source nor the listing.
  Listing words are already free, so the check is listing-aware. The prompt is the strict part:
  `CvGuidance.ConciseBullets` says "cut words, not rewriting ... keep word order".
- `Rewrite`: base CVs only, with a fact check instead of a word check.

`CvGuidance.ConciseBullets` is also used by `BaseCvExternalPrompt`, which has no listing.

## Goals / Non-Goals

**Goals:** let both templates of a listing-tailored CV reword bullets with listing terms, using the existing
listing-aware word check as the guard.

**Non-Goals:** no change to base CV behavior or prompts beyond moving one listing-only sentence out of a shared
constant. No new word-check rules, such as a copied-phrase detector. Paragraph and summary rules stay as they are.

## Decisions

1. **Reuse `KeepsSourceWords` for Detailed.** Detailed bullets get the same check as Concise. The listing supplies
   the new vocabulary, the source supplies the facts, and numbers must come from the source. The owner's example
   ("with product, design, and mobile teams" → "within cross-functional product, design, and mobile teams") adds
   only listing words, so it passes with no extra budget.
   *Alternative:* a fact check like `Rewrite`. That is looser than needed and would let generic CV phrasing in.

2. **Make the mode reflect listing tailoring.** Rename `Shorten` to `SourceWords`, since it now covers more than
   shortening. `ModeFor` gains a `listing` flag: `rewrite` gives `Rewrite`; Concise or `listing` gives
   `SourceWords`; anything else gives `Exact`. Tailored-CV callers (the external preview) pass `listing: true`.
   Base CV callers keep the current behavior: Detailed stays `Exact`.
   *Alternative:* a fourth enum value with the same check. It would duplicate the same branch.

3. **Built-in Detailed analysis returns bullet text.** The Detailed entry prompt asks for
   `{"bullets":[{"id","text"}], ...}` like Concise, with the text "tailored, not shortened: about as long as its
   source". The decoder's `DecodeConciseBullets` becomes `DecodeBullets` for both templates, with `listingText`
   built for both. An empty, too-long, or failing text falls back to the source text. An unknown id still fails
   the section. The Detailed paragraph rule is unchanged. `selectedBulletIds` stays in the result, taken from the
   bullets.

4. **One shared prompt rule, `CvGuidance.ListingWording`.** It replaces the listing sentence in `ConciseBullets`
   and the paragraph-only `ListingTerms` in tailored bullet prompts. Wording, roughly: "Where a bullet shows
   something the listing names or values, reword it with the listing's term for it, such as 'with product,
   design, and mobile teams' → 'within cross-functional product, design, and mobile teams'. Keep the candidate's
   facts, voice, and numbers. Use only words from the bullet or the listing, plus a couple of small linking words.
   Never copy a listing sentence, stack several listing phrases into one bullet, or add a term the bullet does not
   show. Leave a bullet as it is when no listing term fits." `ConciseBullets` drops its listing sentence and
   relaxes "not rewriting / keep word order" to allow these swaps. The base CV prompt keeps the cutting rule,
   without the listing sentence it could not use anyway. `ListingTerms` stays for the Detailed paragraph.

5. **External prompt.** Detailed `bulletRules` switches from "preserve the exact original bullet text" to the
   tailored-bullet rule plus `ListingWording`. `bulletLanguage` for Detailed becomes "Keep each bullet in the
   language it is supplied in". Bump `TailoredCvExternalPromptQueries.Version` to 19. The preview uses
   `ModeFor(template, listing: true)`, so `requireSourceBulletText` is false and `KeepSourceWordingAsync` runs for
   Detailed too.

## Risks / Trade-offs

- [The model over-tailors, so bullets read like the listing] → The prompt forbids copied sentences and stacked
  phrases, the word check limits unsupported words, and the user reviews every suggestion before saving.
- [Inflections count as new words ("streamline" vs "streamlined")] → The existing budget of 2 covers small
  changes. A larger reword falls back to the source text, which is the safe direction.
- [The UI may treat Detailed bullets as unchanged profile text] → `bullets[].text` already exists. The UI notes
  will say it can now differ.
- [Uncommitted CV-prompt work in the same files] → Build on the working tree and don't revert those edits.

## Open Questions

_None._
