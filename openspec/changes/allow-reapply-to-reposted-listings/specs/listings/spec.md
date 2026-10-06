# Spec Delta

## Purpose

Logs job ads as listings, each one attempt at one ad, and keeps the same ad from being tracked twice at once while
still allowing a fresh attempt when an ad is reposted.

## ADDED Requirements

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
title (case-insensitive) as possible duplicates, newest first. Listings returned as previous attempts MUST NOT also be
returned as possible duplicates. The listing is saved either way.

#### Scenario: Same company and role, different URL
- **WHEN** listing A at company C with role R exists, and the user creates listing B at C with role R and another URL
- **THEN** B is saved and A is returned as a possible duplicate

#### Scenario: Previous attempt is not a possible duplicate
- **WHEN** REJECTED listing A at company C with role R has URL X, and the user creates listing B at C with role R and URL X
- **THEN** A is returned as a previous attempt and not as a possible duplicate
