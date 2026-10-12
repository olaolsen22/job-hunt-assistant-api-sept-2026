# Listings Spec Delta

## ADDED Requirements

### Requirement: Batch listing URL check
The API SHALL expose authenticated query `checkListingUrls(urls: [String!]!)`, returning one result per input in input order. Each result SHALL contain zero-based `index`, original `url`, `normalizedUrl`, `existingListingIds` sorted ascending, nullable `duplicateOfIndex`, and `shouldProcess`. The check SHALL accept URLs alone, without requiring job details.

#### Scenario: New URL
- **WHEN** a user checks one URL that has no saved match
- **THEN** the result has index 0, the original and normalized URLs, empty `existingListingIds`, null `duplicateOfIndex`, and `shouldProcess: true`

#### Scenario: Ordered results
- **WHEN** a user checks three URLs in a specified order
- **THEN** three results are returned in that order with indices 0, 1, and 2

### Requirement: Saved URL matches include every listing state
The check SHALL match normalized URLs against only the authenticated user's saved listings, including archived listings and all application statuses. Every same-URL listing id SHALL be returned. A result SHALL have `shouldProcess: true` exactly when it has no saved match and no earlier same-URL input. This recommendation SHALL NOT alter listing creation or update rules.

#### Scenario: Open or archived match
- **WHEN** a checked URL belongs to the user's open listing, including an archived one
- **THEN** that listing id is returned and `shouldProcess` is false

#### Scenario: Closed attempts
- **WHEN** a checked URL belongs to the user's REJECTED, WITHDRAWN, or ACCEPTED listings
- **THEN** all matching ids are returned ascending and `shouldProcess` is false
- **AND** the existing ability to create a new attempt at a URL used only by closed listings remains available

#### Scenario: Another user's URL
- **WHEN** a checked URL is saved only by another user
- **THEN** no saved match is returned and the first occurrence has `shouldProcess: true`

#### Scenario: Authentication required
- **WHEN** an unauthenticated client requests the check
- **THEN** access is denied without returning matching listing ids

### Requirement: Normalized repeats within the batch
The check SHALL use listing URL normalization for both saved matching and batch repeats. `duplicateOfIndex` SHALL identify the first earlier input with the same normalized URL, or be null for its first occurrence. Batch repetition and saved matches SHALL both be reported when both apply. Only exact normalized URL equality SHALL suppress processing.

#### Scenario: Tracking variants
- **WHEN** the batch contains `https://Jobs.example.com/ad/1/?utm_source=x&id=7#apply` followed by `https://jobs.example.com/ad/1?id=7`, and neither is saved
- **THEN** both normalized URLs equal `https://jobs.example.com/ad/1?id=7`
- **AND** only the first has `shouldProcess: true`, while the second has `duplicateOfIndex: 0`

#### Scenario: Repeated saved URL
- **WHEN** inputs at indices 0, 2, and 3 match the same saved URL
- **THEN** all three return the saved ids and `shouldProcess: false`
- **AND** indices 2 and 3 have `duplicateOfIndex: 0`, while index 0 has null

#### Scenario: Meaningful URL differences
- **WHEN** inputs differ by meaningful query parameters, path case, or host and remain different after normalization
- **THEN** they are not treated as repeats of each other, even if the jobs share a title, company, or role

### Requirement: Bounded and validated URL batches
A request SHALL contain 1–100 URLs. Every URL SHALL be trimmed for validation and normalization, be an absolute HTTP(S) URL, and contain at most 2048 characters after trimming. Invalid batch size or any invalid URL SHALL fail the entire check with a GraphQL error and no partial results. Invalid row errors SHALL identify the zero-based input index.

#### Scenario: Invalid sizes
- **WHEN** a request contains zero or 101 URLs
- **THEN** the check returns a GraphQL error and no results

#### Scenario: Maximum batch
- **WHEN** a request contains 100 valid URLs
- **THEN** it returns 100 results

#### Scenario: Invalid URL among valid inputs
- **WHEN** index 1 contains a blank, relative, non-HTTP(S), malformed, or overlength URL
- **THEN** the entire check fails with an error identifying index 1

#### Scenario: Surrounding whitespace
- **WHEN** an input has whitespace surrounding a valid URL
- **THEN** normalization ignores that whitespace while `url` preserves the submitted string

### Requirement: URL checks have no processing side effects
The check SHALL perform no writes, page fetches, or AI calls. It SHALL NOT create listings, parse jobs, LLM usage, or persistent automation history. Repeated checks of an unsaved URL SHALL continue recommending processing until a saved listing matches; recommendations SHALL NOT reserve URLs or guarantee a later save.

#### Scenario: Repeat across requests
- **WHEN** the user checks a new URL twice in separate requests without saving it
- **THEN** both checks return `shouldProcess: true` and create no records

#### Scenario: Save after checking
- **WHEN** the user saves a checked URL and later checks it again
- **THEN** the later check returns the saved listing id and `shouldProcess: false`

#### Scenario: Concurrent save
- **WHEN** another request saves the URL after the check and before an attempted create
- **THEN** the existing create duplicate rules still apply
