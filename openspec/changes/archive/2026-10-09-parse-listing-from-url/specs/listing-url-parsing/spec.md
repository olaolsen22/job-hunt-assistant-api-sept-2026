# Spec Delta

## Purpose

Lets the owner re-parse a saved listing from its own URL: the API fetches the page, extracts the one posting on it
and returns the same parse result as pasted text, so the existing preview and edit step is unchanged.

## ADDED Requirements

### Requirement: Parse a saved listing from its URL
The system SHALL provide a mutation that takes a listing id, fetches the page at that listing's stored URL, runs a
full-mode parse on the extracted text and returns the finished parse job in the same shape `parseListing` returns.
The client SHALL NOT supply a URL; the stored one is the only source.

#### Scenario: Listing whose page has a JobPosting
- **WHEN** the owner parses a saved listing whose URL points at a page that contains a JobPosting
- **THEN** a full-mode parse job is returned with the model's JSON result
- **AND** the job's raw text is the extracted text, so it records what the model saw

#### Scenario: Usage is logged
- **WHEN** a parse from a listing's URL calls the model
- **THEN** one usage row is recorded for the call, like a pasted-text parse

#### Scenario: Another user's or missing listing
- **WHEN** the id belongs to another user or doesn't exist
- **THEN** the mutation returns a typed not-found error and nothing is fetched or created

### Requirement: Fetching is limited to public pages
The system SHALL refuse to fetch URLs that resolve to loopback, private, link-local or otherwise non-public
addresses, SHALL re-check this on every redirect hop, and SHALL cap redirects, response size and total time.

#### Scenario: Loopback target
- **WHEN** the listing's URL is `http://127.0.0.1:5000/admin` or a hostname that resolves to a private address
- **THEN** no request is made to it and the mutation returns a typed "can't read that page" error

#### Scenario: Redirect to a private address
- **WHEN** the listing's URL redirects to a private address
- **THEN** the redirect is not followed and the mutation returns the same typed error

#### Scenario: Oversized or slow response
- **WHEN** the response exceeds the size cap or doesn't finish within the time limit
- **THEN** the mutation returns the typed "can't read that page" error

### Requirement: Prefer the page's structured posting
The system SHALL use the page's JSON-LD `JobPosting`, when present, as the text sent to the model: title,
organization, location, dates and the description with markup removed and entities decoded. Related listings
elsewhere on the page SHALL NOT be sent.

#### Scenario: Page with related listings
- **WHEN** a page has a JobPosting and also lists other jobs below the ad (finn.no cards)
- **THEN** the text sent to the model contains the JobPosting's details and none of the other listings' text

#### Scenario: Description is HTML
- **WHEN** the JobPosting description contains `<p>` tags and entities such as `&#43;`
- **THEN** the text sent to the model has no tags and the entities are decoded (`&#43;` becomes `+`)

### Requirement: Fall back to the page text
When a page has no JobPosting, the system SHALL send the page's visible text with navigation, footer, scripts and
styles removed, and the model's input SHALL say the text came from a web page and that the first posting is the
target.

#### Scenario: Page without structured data
- **WHEN** the page has no JobPosting but has readable content
- **THEN** a parse job runs on the stripped page text with the web-page hint included

#### Scenario: Nothing readable
- **WHEN** stripping the page leaves no usable text (a JS-rendered shell, a login wall)
- **THEN** the mutation returns a typed error telling the owner to paste the text instead, and no job is created

### Requirement: Page failures don't create jobs
When the page can't be read (non-200 status, network failure, timeout, blocked address, too large, no usable text)
the system SHALL return a typed mutation error and SHALL NOT create a parse job or a usage row.

#### Scenario: 403 from the site
- **WHEN** the site answers 403 or 404
- **THEN** the mutation returns "couldn't read that page, paste the text instead"
- **AND** no parse job and no usage row exist for the request

### Requirement: Thinking for full-mode parses
Full-mode parses, from pasted text or from a URL, SHALL run with the model's thinking enabled at a low effort
level. Quick and companies parses SHALL NOT. The output limit SHALL leave room for the thinking so a result isn't
cut off, and any thinking tokens SHALL be counted in the logged usage.

#### Scenario: Full parse requests thinking
- **WHEN** a full-mode parse calls the model
- **THEN** the request asks for low-effort thinking

#### Scenario: Quick parse doesn't
- **WHEN** a quick or companies parse calls the model
- **THEN** the request has no thinking setting
