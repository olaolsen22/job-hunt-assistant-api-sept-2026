# Spec Delta

## Purpose

Lets the owner parse a job ad from a URL while adding a new listing, before anything is saved, with the same fetch
policy and extraction as parsing a saved listing.

## ADDED Requirements

### Requirement: Parse an unsaved listing from a URL
The system SHALL provide a mutation that takes a URL from the client, fetches it under the same fetch policy and
extraction as the saved-listing mutation, runs a full-mode parse and returns the finished parse job in the same shape
`parseListing` returns. The saved-listing mutation SHALL keep taking only a listing id.

#### Scenario: Page with a JobPosting
- **WHEN** the owner parses a public URL whose page contains a JobPosting
- **THEN** a full-mode parse job is returned with the model's JSON result
- **AND** the job's raw text is the extracted text
- **AND** one usage row is recorded

#### Scenario: Invalid URL
- **WHEN** the URL is empty, relative, not `http`/`https`, or longer than 2048 characters
- **THEN** the mutation returns a typed invalid-request error and nothing is fetched or created

#### Scenario: Unreadable or blocked page
- **WHEN** the page can't be read (non-200, timeout, too large, private address, no usable text)
- **THEN** the mutation returns a typed invalid-request error and no job is created
