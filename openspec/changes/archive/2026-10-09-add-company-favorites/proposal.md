# Proposal

## Why

Listings can be favorited to build a shortlist, but companies can't. When you track many companies, you want a short
list of the ones you'd most like to work for, separate from any single listing.

## What Changes

- Add `Company.FavoritedAt` (`DateTime?`, null = not a favorite), the same shape as `Listing.FavoritedAt`.
- Add `setCompanyFavorite(input: {id, isFavorite})` → `Company`; unknown id → `CompanyNotFoundError`. Favoriting twice keeps the first time.
- `Company.favoritedAt` is exposed on the GraphQL type and is filterable and sortable through the existing `companies` query, so a shortlist is `companies(where: {favoritedAt: {neq: null}}, order: [{favoritedAt: DESC}])`.
- One migration adding the nullable column (no backfill: nothing is a favorite yet).

## Capabilities

### New Capabilities
- `company-favorites`: marking and unmarking a company as a favorite, and listing favorites newest first.

### Modified Capabilities

## Impact

- `Features/Companies` (`Company`, `CompanyMutations`), one migration, `schema.graphql`, `docs/STATUS.md`, and tests.
- No new packages. Named `company-favorites` rather than a change to `companies` because `companies` is still an unarchived spec from `add-company-bulk-create`.
