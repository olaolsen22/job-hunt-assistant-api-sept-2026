# Proposal

## Why

Parsed listing titles are copied verbatim from the ad, so they are often long ("Full-stack-utviklar / Full-stack
Developer – Bergen, 100 % fast") and in Norwegian. A short, plain-English title is easier to scan in the tracker.

## What Changes

- Both listing prompts (full and quick) ask for `title` as a short, direct job title in natural English, translated
  when the ad is in another language, instead of "exactly as written in the ad, original language".
- The title keeps what identifies the job (seniority word, specialty, e.g. "Senior Backend Developer (.NET)") and drops
  company name, location, percentage/employment type, slogans and duplicated bilingual variants.
- Both prompt versions are bumped (`Version` 7 → 8, `QuickVersion` 4 → 5) so the server-side parse cache doesn't
  serve old-style titles.
- Existing listings are not changed.

## Capabilities

### New Capabilities

### Modified Capabilities
- `listing-parsing`: the "Formal job title" requirement becomes a short English job title requirement.

## Impact

- `src/JobHunt.Api/Features/Listings/ListingParsePromptQueries.cs` (both prompts' output comment + `title` rule, versions).
- `tests/JobHunt.Api.Tests/Features/Listings/ListingParsePromptTests.cs`.
- Duplicate detection also matches on normalized title; new English titles won't title-match older Norwegian ones,
  but the same-company + same-job-role match still catches those.
