# listings Specification

## Purpose
Logs job ads as listings, each one attempt at one ad, and keeps the same ad from being tracked twice at once while
still allowing a fresh attempt when an ad is reposted.

## Requirements

### Requirement: URL normalization
A listing's URL SHALL be stored normalized: lowercase scheme and host, no default port, no `utm_*`, `fbclid` or `gclid`
query parameters, no fragment and no trailing slash. Other query parameters MUST be kept. Duplicate rules compare
normalized URLs.

#### Scenario: Tracking parameters removed
- **WHEN** the user creates a listing with URL `https://Jobs.example.com/ad/1/?utm_source=x&id=7#apply`
- **THEN** the stored URL is `https://jobs.example.com/ad/1?id=7`

### Requirement: One open listing per URL
A user SHALL NOT have two open listings with the same normalized URL. Creating a listing, or updating an open listing,
with a URL used by another open listing of the same user MUST fail with `DuplicateListingError` carrying that
listing's id. Archived listings count, as long as they are open.

#### Scenario: Same URL as an open listing
- **WHEN** a listing with status APPLIED has URL X, and the user creates another listing with URL X
- **THEN** the request fails with `DuplicateListingError` whose `existingListingId` is the APPLIED listing

#### Scenario: Same URL as a saved, archived listing
- **WHEN** a listing with status SAVED is archived and has URL X, and the user creates another listing with URL X
- **THEN** the request fails with `DuplicateListingError` whose `existingListingId` is the archived listing

#### Scenario: Update to an open listing's URL
- **WHEN** the user updates open listing A to the URL of open listing B
- **THEN** the request fails with `DuplicateListingError` whose `existingListingId` is B

#### Scenario: Closed listing may share an open listing's URL
- **WHEN** the user updates closed listing A to the URL of open listing B
- **THEN** the update succeeds

### Requirement: New attempt at a reposted ad
Creating or updating a listing SHALL succeed when its URL is used only by closed listings of the same user.

#### Scenario: Re-apply after rejection
- **WHEN** the only listing with URL X has status REJECTED, and the user creates a listing with URL X
- **THEN** a new listing with status SAVED is created and the old listing is unchanged

#### Scenario: Re-apply after withdrawing or accepting
- **WHEN** the only listings with URL X have status WITHDRAWN or ACCEPTED, and the user creates a listing with URL X
- **THEN** a new listing is created

### Requirement: Previous attempts on create
Creating a listing SHALL return the user's closed listings with the same normalized URL as previous attempts, newest
first, and an empty list when there are none.

#### Scenario: Previous attempt returned
- **WHEN** listing A with URL X is REJECTED, and the user creates listing B with URL X
- **THEN** the result contains B and lists A as a previous attempt

#### Scenario: First attempt
- **WHEN** the user creates a listing with a URL no other listing uses
- **THEN** the previous attempts list is empty

### Requirement: Possible duplicates on create
Creating a listing SHALL return the user's other listings at the same company with the same job role or the same
title (case-insensitive) as possible duplicates, newest first. A listing at a different company MUST NOT be returned,
whatever its title, role or position in a batch. Listings returned as previous attempts MUST NOT also be returned as
possible duplicates. The listing is saved either way.

#### Scenario: Same company and role, different URL
- **WHEN** listing A at company C with role R exists, and the user creates listing B at C with role R and another URL
- **THEN** B is saved and A is returned as a possible duplicate

#### Scenario: Previous attempt is not a possible duplicate
- **WHEN** REJECTED listing A at company C with role R has URL X, and the user creates listing B at C with role R and URL X
- **THEN** A is returned as a previous attempt and not as a possible duplicate

#### Scenario: Same title at different companies
- **WHEN** listing A "Senior Developer" at company C1 exists, and the user creates listing B "Senior Developer" at
  company C2 with another URL
- **THEN** B is saved and possible duplicates is empty

#### Scenario: Same title at different companies in one batch
- **WHEN** one request creates "Senior Developer" at C1 and then "Senior Developer" at C2 with different URLs
- **THEN** both are saved and neither payload returns the other as a possible duplicate

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
