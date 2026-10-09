# Proposal

## Why

Analyzing a real ad (FORTLOQ, arbeidsplassen.nav.no) with the full listing prompt gave wrong or empty fields, from
Gemini and from ChatGPT on the copy-paste path. Both models were following the prompt; the prompt itself is the
problem. It never says which text is the title, has no `workMode` rule, ignores relative deadlines ("Søk senest i
dag"), and mixes a blanket "Don't guess" with nothing that stops invented facts. As a result, seniority stays null for
"erfaren, minst 5 år" while a skill the ad never names (PHP) gets through.

## What Changes

- Title: the formal job title (a "Stillingstittel"/"Job title" field when present), not a headline or slogan.
- Seniority: inferred from experience when not stated: 0–1 years/graduate → JUNIOR, 2–4 → MID,
  5+/"erfaren"/"experienced" → SENIOR, leads people → LEAD. Replaces "not from years alone".
- Global rule: "Use null when the ad doesn't say. Don't guess." becomes "infer from clear signals, null only when the
  ad gives nothing; never invent names, dates or URLs".
- New `workMode` rule (hjemmekontor/hybrid → HYBRID, fjernarbeid/fully remote → REMOTE, explicit onsite → ONSITE;
  flexible hours alone → null).
- Deadline: relative dates ("i dag"/"today", "i morgen"/"tomorrow") → DATE.
- `postedOn`: publication date only; "Sist endret"/"last modified" doesn't count.
- Skills: only names that appear in the posting.
- Quick-add prompt gets the shared rules (title, postedOn, relative deadline, no invented facts) so the two prompts
  don't drift.
- `ListingParsePromptQueries.Version` 4 → 5, `QuickVersion` 2 → 3.

No schema, GraphQL shape or output JSON change.

## Capabilities

### New Capabilities
- `listing-parsing`: what the listing parse prompts ask the model to extract and how. This is the same capability the
  in-flight `parse-timeout-and-doc-cleanup` change introduces; this change adds the prompt-rule requirements to it.

### Modified Capabilities
None. `openspec/specs/` has no archived specs yet.

## Impact

- `src/JobHunt.Api/Features/Listings/ListingParsePromptQueries.cs`: prompt text and the two version constants.
- `tests/JobHunt.Api.Tests/Features/Listings/ListingParsePromptTests.cs`: version asserts and new rule asserts.
- `docs/STATUS.md`: version numbers.
- The UI is unaffected: same prompt queries and output shape. A copy-paste prompt cached in the UI picks up the new
  version on its next fetch.
