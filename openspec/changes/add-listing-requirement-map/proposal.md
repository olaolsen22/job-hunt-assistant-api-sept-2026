# Proposal

## Why

The application flow is meant to be: fill the profile, analyze the job, tailor truthfully, check it still sounds like
the owner. The "analyze the job" step does not exist. Listing parsing extracts fields and skills, but nothing states
what the listing needs, which profile evidence answers each need, and which needs are honest gaps. Every tailored CV
section call therefore re-reads the listing and judges relevance alone, and the owner cannot see why a CV came out the
way it did or decide how much effort a listing deserves.

This is the first of four planned changes that put CV and cover letter work on one spine (map, synthesis rework,
templates on shared rules, cover letter reading the map). It comes first because the later ones consume it.

## What Changes

- New per-listing **requirement map**: a list of needs taken from the listing, each with a kind, whether the listing
  requires or prefers it, a match state (evidenced, partial, gap), references to the profile entries and bullets that
  support it, and a short note.
- The map follows the existing review pattern: built-in AI generation or a copy-paste external prompt returns an
  unsaved candidate, the owner reviews and corrects it, and only an explicit save stores it. One saved map per
  listing; saving replaces it.
- The server validates the map: evidence references must belong to the owner's profile, a gap carries no evidence, and
  a need marked evidenced or partial must point at evidence or explain itself in a note.
- The prompt sends only allowlisted listing and profile data, like the CV prompts: no contact details, date of birth,
  nationality, work authorization, street address or salary.
- The saved map can be deleted.

Out of scope here (later changes): tailored CV, base CV and cover letter prompts reading the saved map; any change to
wording modes or guards; deterministic skill/years matching and dashboard use of gaps.

## Capabilities

### New Capabilities

- `listing-requirement-map`: the per-listing map of what a listing needs, the profile evidence for each need and the
  remaining gaps, including how it is generated, previewed, validated, saved, read and deleted.

### Modified Capabilities

None. Listings, listing parsing and tailored CV drafts keep their current requirements.

## Impact

- New feature slice `src/JobHunt.Api/Features/RequirementMaps/` (entity, content records, prompt, generator, queries
  and mutations) and tests under `tests/JobHunt.Api.Tests/Features/RequirementMaps/`.
- `AppDbContext`: new set and user query filter. One additive migration (new table, no changes to existing tables).
- `schema.graphql`: two queries and four mutations added; nothing existing changes.
- One new LLM operation logged through the existing usage and AI safeguard flow. No new packages or config keys.
- Docs: a short UI guide and the STATUS index.
