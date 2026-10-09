# Design

## Context

Both prompts are raw strings in `ListingParsePromptQueries`. The copy-paste path (`listingParsePrompt`,
`listingQuickParsePrompt`) and the server-side `ListingParser` share them, so a single text edit covers both paths.
Tests only assert on the prompt text and version. Model output isn't tested, since the test LLM is a fake.

## Goals / Non-Goals

**Goals:**
- Fix the rule gaps found on the FORTLOQ ad without changing the output JSON shape.

**Non-Goals:**
- New listing fields (salary, contact, education).
- Switching models or changing temperature/JSON mode.
- Automated quality checks against real model output.

## Decisions

- **Infer, but don't invent.** "Don't guess" made the models drop clear signals (seniority, work mode), and still
  didn't stop invented skills. The line between them: inference maps wording that's in the ad onto an enum, while
  invention adds names, dates or URLs that aren't there. Alternative considered: keep "Don't guess" and add
  exceptions per field. Rejected because the global line keeps overriding the field rules.
- **Seniority cutoffs 0–1 / 2–4 / 5+.** The owner picked these over 3+ → SENIOR. They match how Norwegian ads use
  "erfaren" and "minst 5 år". An explicit word in the title still wins, so "Junior … 3 års erfaring" stays JUNIOR.
- **Shared rules go into the quick prompt too.** Title, postedOn, relative deadline and no-invention rules apply to
  quick add as well. Leaving them out would mean "Søk senest i dag" parses differently in the two flows. The quick
  prompt still has no seniority, workMode or skills, to keep its output short.
- **Text asserts, not snapshot.** Following the existing tests: assert key phrases plus the versions, so wording
  tweaks don't break tests.

## Risks / Trade-offs

- [More inference means occasional wrong SENIOR/HYBRID] → The user reviews every parse in the form before saving. A
  wrong enum is one click; a missing one meant re-reading the ad.
- [Longer prompt costs a few more input tokens per parse] → Roughly 150 tokens. Negligible on the Gemini free tier.
- [Gemini returned values that weren't in its input on the 06:50 job] → The no-invention line targets this, but
  can't guarantee it. Not fixable by the prompt alone.
