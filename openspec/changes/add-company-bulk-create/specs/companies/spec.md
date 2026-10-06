# Spec Delta

## Purpose

Lets the user keep a list of companies, each with a name, one industry and any number of locations, whether created
on their own (one or many at a time) or as a side effect of saving a listing.

## ADDED Requirements

### Requirement: Create companies in bulk
`createCompanies` SHALL accept a list of rows `{name, industryName?, locations}` and return one result per row,
`{company, created}`, in the same order. A single company is a list of one. The batch SHALL contain 1 to 100 rows,
otherwise the request fails with `InvalidCompanyError`.

#### Scenario: Several new companies
- **WHEN** the user sends three rows with names that do not exist yet
- **THEN** three companies are created and the result lists them in order, each with `created: true`

#### Scenario: Empty or oversized batch
- **WHEN** the user sends zero rows, or 101 rows
- **THEN** the request fails with `InvalidCompanyError` and nothing is saved

### Requirement: Batches are all-or-nothing
`createCompanies` SHALL validate every row before saving, and SHALL save all rows in one transaction. If any row has a
blank name or an invalid location, the request fails with `InvalidCompanyError` or `InvalidLocationError` and no
company, industry or location from the batch is saved.

#### Scenario: One blank name
- **WHEN** the user sends five rows and the third has a blank name
- **THEN** the request fails with `InvalidCompanyError` and none of the five companies exist afterwards

#### Scenario: One bad country code
- **WHEN** a row has a location with country code `XX`
- **THEN** the request fails with `InvalidLocationError` and nothing from the batch is saved

### Requirement: Existing names are returned, not duplicated
Company names SHALL be unique per user, ignoring case, surrounding spaces and the differences `ToUpperInvariant` folds
(including æøå). A row whose name already exists SHALL return that company with `created: false` and SHALL NOT change
its industry or locations. Rows in the same batch with the same name SHALL collapse into one company, the first row
winning, and the repeats SHALL be returned with `created: false`.

#### Scenario: Re-pasting a list
- **WHEN** the user sends the same list of companies twice
- **THEN** the second request creates nothing, returns every company with `created: false`, and leaves the existing data unchanged

#### Scenario: Different case
- **WHEN** "equinor" exists and a row says " EQUINOR "
- **THEN** the existing company is returned with `created: false`

#### Scenario: Repeat inside one batch
- **WHEN** one batch has "Aker BP" in rows 1 and 4 with different industries
- **THEN** one company is created from row 1, and row 4 is returned with `created: false` for the same company

### Requirement: Industry is found or created by name
A company SHALL have at most one industry. An industry name SHALL be matched against the user's industries the way
company names are, and created when it does not exist. A missing or blank `industryName` SHALL mean no industry.
Rows in one batch naming the same new industry SHALL share one industry.

#### Scenario: New industry
- **WHEN** a row has `industryName` "Energy" and the user has no such industry
- **THEN** the industry is created and the company's `industry.name` is "Energy"

#### Scenario: Existing industry, different case
- **WHEN** the user has "Energy" and a row says "energy"
- **THEN** the company uses the existing industry and no second industry is created

#### Scenario: Shared new industry in one batch
- **WHEN** two rows in a batch both name the new industry "Shipping"
- **THEN** the batch succeeds and one "Shipping" industry exists

### Requirement: A company has several locations
A company SHALL have any number of locations, each `{city?, countryCode}` with an ISO 3166 country code, found or
created the same way as a listing's locations. Locations repeated within a row SHALL be collapsed. There is no
separate head office.

#### Scenario: Two locations
- **WHEN** a row has locations Oslo (NO) and Bergen (NO)
- **THEN** the company's `locations` contains both

#### Scenario: Repeated location in a row
- **WHEN** a row lists Oslo (NO) twice
- **THEN** the company has one Oslo location

#### Scenario: Same new location in two rows
- **WHEN** two rows in one batch both list the new location Stavanger (NO)
- **THEN** the batch succeeds and one Stavanger location exists, used by both companies

### Requirement: Update a company
`updateCompany` SHALL replace a company's name, industry and locations with the values sent: a missing
`industryName` clears the industry and an empty `locations` list clears the locations. Renaming to a name another
company already has SHALL fail with `InvalidCompanyError`, and an unknown id with `CompanyNotFoundError`.

#### Scenario: Replace locations
- **WHEN** a company has Oslo and the user updates it with only Bergen
- **THEN** the company's `locations` contains only Bergen

#### Scenario: Clear the industry
- **WHEN** the user updates a company without `industryName`
- **THEN** the company has no industry

#### Scenario: Rename onto an existing name
- **WHEN** the user renames "Aker BP" to "Equinor" and "Equinor" exists
- **THEN** the request fails with `InvalidCompanyError` and "Aker BP" is unchanged

### Requirement: Existing head offices become locations
Each company that had a head-office location before this change SHALL have that location as its only location
afterwards, with nothing else about it changed.

#### Scenario: Upgrade
- **WHEN** a company was saved with head office Oslo before the upgrade
- **THEN** its `locations` contains Oslo afterwards

### Requirement: Read companies and industries
`Company` SHALL expose `industry` and `locations`, and `companies` SHALL be filterable by industry through `where`.
`industries(search)` SHALL list the user's industries ordered by name, matching `search` the way `companies(search)`
matches company names.

#### Scenario: Filter by industry
- **WHEN** the user queries `companies(where: {industry: {name: {eq: "Energy"}}})`
- **THEN** only companies with that industry are returned

#### Scenario: Industry autocomplete
- **WHEN** the user queries `industries(search: "ene")`
- **THEN** industries whose name contains "ene", ignoring case, are returned ordered by name

### Requirement: Companies and industries belong to one user
Companies, industries and their locations SHALL only be visible to, and matched against, the user who owns them.

#### Scenario: Same name for another user
- **WHEN** user A has "Equinor" and user B creates "Equinor"
- **THEN** B gets a new company with `created: true` and A's company is unchanged
