# Design

## Context
Companies and listings exist; listings belong to one company. Parsing returns JSON that the UI feeds into
`createListing`/`updateListing`, it never writes entities itself.

## Goals / Non-Goals
- Goals: per-company contact people, listing↔contact links, extraction in the full prompt.
- Non-Goals: contact history/interactions log, company-level generic contact fields (website, switchboard), contacts
  in the quick prompt, linking contacts across companies (recruitment agencies — revisit if needed).

## Decisions
- **Contact is a table under Company, linked to Listing via `ListingContact`.** One recruiter often covers several
  ads; storing contacts on the listing would duplicate them and lose that. Real FKs, not JSON, since we join on it.
  `Contact.CompanyId` cascade on company delete; `ListingContact` cascades from both sides.
- **Upsert by normalized name inside `ListingInput`.** Mirrors how companies/skills/locations are already resolved by
  name, so the parsed JSON flows through unchanged. Fill-empty-only merge so a parse can't clobber hand edits.
- **Unique index `(CompanyId, NormalizedName)`**, reusing `Names.Normalize`.
- **Separate `addContact`/`updateContact`/`deleteContact` mutations** for managing contacts on the company page;
  typed errors `DuplicateContactException`, `ContactNotFoundException`, `InvalidContactException`.
- **Validation**: name required, field length caps; email not format-checked beyond `@` presence (ads are messy).
- **Prompt**: add `contacts` to full prompt shape + a rule; bump its `Version`.

## Risks / Trade-offs
- Two people with the same name at one company collapse into one → acceptable for a personal tracker.
- Model may hallucinate a phone number → rule says copy from text only; user reviews before save.

## Migration Plan
`make migration name=AddContacts` (backs up DB). Additive tables only; no data migration.
