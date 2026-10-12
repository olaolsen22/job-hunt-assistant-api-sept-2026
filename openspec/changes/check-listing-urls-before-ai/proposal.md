# Proposal

## Why

The external scheduled automation repeatedly encounters jobs already saved in the API or repeated within its master list. Checking collected URLs locally before the AI reads each job avoids spending tokens on those repeats.

## What Changes

- Add an authenticated, read-only GraphQL batch URL check, using the same normalization as listing creation.
- Return one result per input URL, identifying saved listing matches, earlier occurrences within the batch, and whether the automation should process that row.
- Compare against all of the user's saved listings, including archived and closed attempts. This discovery check does not change the rules that allow creating a new attempt at a closed listing's URL.
- Document the collect URLs → check locally → process eligible jobs with AI workflow, including chunking and authentication.
- Scope excludes fuzzy matching across different URLs, storage of unsaved automation history, a scheduler, external-service configuration, UI changes, and automatic saving or parsing.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `listings`: Add batch URL preflight matching against saved listings and repeats within the request.

## Impact

- Listings GraphQL queries and a shared URL normalization helper extracted from `ListingMutations` without changing stored URL semantics.
- Additive `schema.graphql` contract and integration tests for validation, user isolation, ordering, normalization, and read-only behavior.
- API usage documentation for the external automation. No migrations, new packages, credentials, or LLM provider changes.
