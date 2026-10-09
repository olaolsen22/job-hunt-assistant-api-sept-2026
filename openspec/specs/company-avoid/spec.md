# company-avoid Specification

## Purpose
Lets the user mark companies they want to stay away from, so listings from those companies can be flagged, without
hiding anything.

## Requirements

### Requirement: Mark and unmark a company as avoided
`setCompanyAvoided` SHALL accept `{id, isAvoided}` and return the company. With `isAvoided: true` the company gets an
`avoidedAt` time; with `false` it is cleared to null. An unknown id, or another user's company, SHALL fail with
`CompanyNotFoundError`.

#### Scenario: Avoid a company
- **WHEN** the user sets `isAvoided: true` on a company that is not avoided
- **THEN** the returned company has a non-null `avoidedAt`

#### Scenario: Stop avoiding a company
- **WHEN** the user sets `isAvoided: false` on an avoided company
- **THEN** the returned company has `avoidedAt: null`

#### Scenario: Unknown or foreign company
- **WHEN** the user sets avoided on an id that does not exist for them
- **THEN** the request fails with `CompanyNotFoundError`

### Requirement: Avoiding twice keeps the first time
Avoiding an already-avoided company SHALL leave `avoidedAt` unchanged.

#### Scenario: Repeat avoid
- **WHEN** the user avoids a company that was avoided earlier
- **THEN** `avoidedAt` is still the earlier time

### Requirement: Avoided companies can be listed
The `companies` query SHALL allow filtering and sorting by `avoidedAt`.

#### Scenario: Filter avoided companies
- **WHEN** the user avoids company A, leaves company B alone, and queries companies where `avoidedAt` is not null
- **THEN** the result is A only

### Requirement: Listings from avoided companies stay visible
Avoiding a company SHALL NOT hide, block or change its listings. A listing's company SHALL expose `avoidedAt`, so the
client can flag it.

#### Scenario: Listing shows the avoided company
- **WHEN** the user avoids a company that has a listing and queries `listings { company { avoidedAt } }`
- **THEN** the listing is returned and its `company.avoidedAt` is non-null

#### Scenario: New listing for an avoided company
- **WHEN** the user creates a listing for an avoided company
- **THEN** the listing is saved as usual

### Requirement: Avoided is independent of favorite
A company SHALL be able to be avoided and favorite at the same time. Setting one SHALL NOT change the other.

#### Scenario: Avoid a favorite
- **WHEN** the user avoids a favorite company
- **THEN** the company has non-null `avoidedAt` and its `favoritedAt` is unchanged

### Requirement: Avoided state is not an edit
Changing avoided SHALL NOT change the company's name, industry, locations or notes. `updateCompany` and
`createCompanies` SHALL NOT change avoided state.

#### Scenario: Update keeps avoided
- **WHEN** the user renames an avoided company with `updateCompany`
- **THEN** its `avoidedAt` is unchanged

#### Scenario: Bulk create keeps avoided
- **WHEN** `createCompanies` includes a row matching an avoided company
- **THEN** that company is returned with `created: false` and its `avoidedAt` is unchanged
