# Design

## Context

See proposal.md for motivation. `Listing.FavoritedAt` (`DateTime?`) and `ListingMutations.SetListingFavoriteAsync`
already do this for listings: `??=` on favorite so the first time stays, `null` to clear. `Company` has no timestamp
fields; `companies` is already paged, projected, filterable and sortable, so a new scalar shows up there for free.
Requirements are in `specs/company-favorites`.

## Goals / Non-Goals

**Goals:**
- Same behavior and naming as the listing favorite, so the UI treats both alike.

**Non-Goals:**
- Ranking favorites, named lists, or favorites on industries (PLAN §9 only mentions ranking for listings).
- A `favoriteCompanyCount` or similar aggregate.

## Decisions

**`FavoritedAt` timestamp, not a bool.**
Matches `Listing.FavoritedAt` and lets the shortlist sort by when you favorited. A bool would need a second column
later for ordering.

**One mutation `setCompanyFavorite(id, isFavorite)`.**
Mirrors `setListingFavorite`; idempotent, so the UI can call it from a toggle without reading state first. Separate
favorite/unfavorite mutations would double the surface for no gain.

**Reuse `CompanyNotFoundException` via `[Error<>]`.**
It already maps to `CompanyNotFoundError` for `updateCompany`. The query filter hides other users' companies, so a
foreign id is simply not found.

**No extra index.**
Favorites are a small subset of a per-user company list, and listing filters by `favoritedAt` the same way without
one.

## Risks / Trade-offs

- [`createCompanies` returns an existing company untouched] → Favorite state is not an import field, so re-pasting a list never clears a favorite.
- [Deleting a company drops the favorite with it] → Companies can't be deleted today; nothing to handle.

## Migration Plan

1. `make migration name=AddCompanyFavorites`: adds nullable `FavoritedAt` to `Companies`, no backfill.
2. Roll back by reverting the migration; only favorite marks are lost.
