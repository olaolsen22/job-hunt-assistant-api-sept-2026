# Spec Delta

## Purpose

Logs job ads as listings and lets the user find them again: deadline types, the server-owned effective due date, the
default order, "closing soon", date ranges and text search.

## ADDED Requirements

### Requirement: Deadline types
A listing's deadline type SHALL be one of DATE, ASAP or UNSPECIFIED. `deadlineOn` MUST be set exactly when the type is
DATE; otherwise the request fails with `InvalidListingError`.

#### Scenario: Date without a date
- **WHEN** the user creates a listing with deadline type DATE and no `deadlineOn`
- **THEN** the request fails with `InvalidListingError`

#### Scenario: Rolling is no longer a type
- **WHEN** a client sends deadline type ROLLING
- **THEN** the request fails GraphQL validation

### Requirement: Existing rolling listings become ASAP
Listings stored with the former ROLLING deadline type SHALL read as ASAP after the upgrade, with all other fields
unchanged.

#### Scenario: Upgrade
- **WHEN** a listing was saved with deadline type ROLLING before the upgrade
- **THEN** it is returned with deadline type ASAP afterwards

### Requirement: Parse prompts map rolling deadlines to ASAP
Both parse prompts (full and quick) SHALL map "Snarest", "ASAP", "straks", "Løpende", "fortløpende" and "rolling" to
ASAP, and SHALL NOT offer ROLLING. The full prompt's version SHALL be 4 and the quick prompt's version 2.

#### Scenario: Prompt content
- **WHEN** the user requests `listingParsePrompt` or `listingQuickParsePrompt`
- **THEN** the instructions do not contain `ROLLING`, map "Løpende" to ASAP, and the versions are 4 and 2

### Requirement: Effective due date
Every listing SHALL have an effective due date, exposed as `dueOn`: `deadlineOn` for DATE, `postedOn` + 14 days for
ASAP, and `postedOn` + 30 days for UNSPECIFIED.

#### Scenario: Due date per type
- **WHEN** listings are posted on 2026-10-01 with type ASAP, type UNSPECIFIED, and type DATE with `deadlineOn` 2026-10-20
- **THEN** their `dueOn` is 2026-10-15, 2026-10-31 and 2026-10-20

### Requirement: Expired listings
A listing SHALL count as expired when its effective due date is before today (UTC). Expiry affects only ordering and
the due filter; it MUST NOT change status, archive state or any stored field.

#### Scenario: Old listing without a deadline
- **WHEN** a listing with type UNSPECIFIED was posted 40 days ago
- **THEN** it counts as expired and its status is unchanged

### Requirement: Default listing order
Without a client `order`, listings SHALL be ordered with not-expired listings first by effective due date ascending,
then expired listings by effective due date ascending; ties are broken by newest created first. A client `order`
replaces this order.

#### Scenario: Mixed deadline types
- **WHEN** today is T and listings exist with: DATE due T+2; ASAP posted T-10; UNSPECIFIED posted T-1; DATE due T+30;
  ASAP posted T-20; DATE due T-1
- **THEN** the order is: DATE T+2, ASAP posted T-10, UNSPECIFIED posted T-1, DATE T+30, ASAP posted T-20, DATE T-1

#### Scenario: Client order
- **WHEN** the client orders listings by title ascending
- **THEN** listings come back in title order, not due-date order

### Requirement: Closing soon filter
`listings(dueWithinDays: N)` SHALL return only listings whose effective due date is between today and today + N
days, inclusive. Expired listings MUST NOT be returned. A negative N MUST fail with an error.

#### Scenario: Within the window
- **WHEN** listings are due today, in 7 days and in 8 days, and the client asks for `dueWithinDays: 7`
- **THEN** the first two are returned

#### Scenario: Expired excluded
- **WHEN** a listing is due yesterday and the client asks for `dueWithinDays: 7`
- **THEN** it is not returned

#### Scenario: Negative window
- **WHEN** the client asks for `dueWithinDays: -1`
- **THEN** the query returns an error

### Requirement: Text search
`listings(search: S)` SHALL return listings whose title, company name or job role name contains S, case-insensitively
including Norwegian letters (æ, ø, å). The description MUST NOT be searched. A blank S MUST return listings as if no
search was given. Search combines with `where`, `archived` and `dueWithinDays`.

#### Scenario: Case-insensitive with Norwegian letters
- **WHEN** a listing's title is "Utvikler på Økonomi" and the client searches "ØKONOMI" or "økonomi"
- **THEN** the listing is returned for both

#### Scenario: Company or role match
- **WHEN** the client searches part of a listing's company name or job role name
- **THEN** the listing is returned

#### Scenario: Description not searched
- **WHEN** a word appears only in a listing's description and the client searches it
- **THEN** the listing is not returned

#### Scenario: Combined with filters
- **WHEN** the client searches "backend" with `where: { status: { eq: APPLIED } }`
- **THEN** only APPLIED listings matching "backend" are returned

### Requirement: Posted and deadline date ranges
Listings SHALL be filterable by `postedOn` and by `deadlineOn` ranges (`gte`/`lte`) through `where`, each on its own or
together. A `deadlineOn` range matches only listings with deadline type DATE.

#### Scenario: Posted range
- **WHEN** the client filters `postedOn` between 2026-09-01 and 2026-09-30
- **THEN** only listings posted in September 2026 are returned

#### Scenario: Deadline range skips undated listings
- **WHEN** the client filters `deadlineOn` between today and today + 14
- **THEN** ASAP and UNSPECIFIED listings are not returned
