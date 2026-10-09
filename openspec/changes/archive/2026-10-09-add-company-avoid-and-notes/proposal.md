# Proposal

## Why

Some companies are ones to stay away from, based on personal experience. Right now nothing records that, so a listing
from one of them looks like any other, and the reason is forgotten in a few months. Other companies have notes worth
keeping too ("good mentors", "recruiter was slow"), and today there's nowhere on a company to put them.

## What Changes

- Add `Company.AvoidedAt` (`DateTime?`, null = not avoided), the same shape as `Company.FavoritedAt`.
- Add `Company.Notes` (`string?`, Markdown), one free-text field for any note on a company: why it's avoided, or
  anything good or bad.
- Add `setCompanyAvoided(input: {id, isAvoided})` → `Company`. Avoiding twice keeps the first time.
- Add `setCompanyNotes(input: {id, notes})` → `Company`. Trimmed, blank or null clears, max 10,000 characters.
- Both fields are exposed on the `Company` GraphQL type, so `companies(where: {avoidedAt: {neq: null}})` lists avoided
  companies and `listing.company.avoidedAt` drives a badge on listings.
- Avoided and favorite are independent: a company can be both, either, or neither.
- Avoided companies' listings stay visible everywhere. Nothing is hidden or blocked, and quick add doesn't warn.
- `updateCompany` and `createCompanies` never change either field.
- One migration adding two nullable columns (no backfill).

## Capabilities

### New Capabilities
- `company-avoid`: marking and unmarking a company as one to avoid, and listing avoided companies.
- `company-notes`: keeping a free-text Markdown note on a company.

### Modified Capabilities

## Impact

- `Features/Companies` (`Company`, `CompanyMutations`), one migration (`AddCompanyAvoidAndNotes`), `schema.graphql`,
  `docs/STATUS.md`, and tests.
- No new packages, no breaking schema changes: two new fields and two new mutations.
- UI (separate repo): badge from `company.avoidedAt`, a toggle, and a notes editor. The UI sanitizes Markdown on
  render, as it already does for interview notes.
