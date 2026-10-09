# company-favorites Specification

## Purpose
Lets the user mark companies as favorites and list them as a shortlist, newest favorite first, independent of any
listing.

## Requirements

### Requirement: Mark and unmark a company as a favorite
`setCompanyFavorite` SHALL accept `{id, isFavorite}` and return the company. With `isFavorite: true` the company gets a
`favoritedAt` time; with `false` it is cleared to null. An unknown id, or another user's company, SHALL fail with
`CompanyNotFoundError`.

#### Scenario: Favorite a company
- **WHEN** the user sets `isFavorite: true` on a company that is not a favorite
- **THEN** the returned company has a non-null `favoritedAt`

#### Scenario: Unfavorite a company
- **WHEN** the user sets `isFavorite: false` on a favorite
- **THEN** the returned company has `favoritedAt: null`

#### Scenario: Unknown or foreign company
- **WHEN** the user sets a favorite on an id that does not exist for them
- **THEN** the request fails with `CompanyNotFoundError`

### Requirement: Favoriting twice keeps the first time
Favoriting an already-favorite company SHALL leave `favoritedAt` unchanged, so the shortlist order does not change.

#### Scenario: Repeat favorite
- **WHEN** the user favorites a company that was favorited earlier
- **THEN** `favoritedAt` is still the earlier time

### Requirement: Favorites form a shortlist
The `companies` query SHALL allow filtering and sorting by `favoritedAt`, so favorites can be listed newest first.

#### Scenario: Shortlist order
- **WHEN** the user favorites company A, then B, then queries favorites ordered by `favoritedAt` descending
- **THEN** the result is B, then A, and non-favorites are excluded

### Requirement: Favorite state is not an edit
Changing a favorite SHALL NOT change the company's name, industry or locations, and editing a company with
`updateCompany` SHALL NOT change its favorite state.

#### Scenario: Update keeps the favorite
- **WHEN** the user renames a favorite company with `updateCompany`
- **THEN** its `favoritedAt` is unchanged
