# Spec Delta

## Purpose

Turns pasted text, such as a list of company names or a company's about page, into proposed company rows, either by
the server's own AI or by a prompt the user takes to an external AI. It only proposes; saving is `createCompanies`.

## ADDED Requirements

### Requirement: Parse companies on the server
`parseListing` SHALL accept mode COMPANIES and return a parse job whose `result`, when it succeeds, is a JSON array of
rows `{name, industryName, locations}` that can be sent to `createCompanies` unchanged. It SHALL NOT create or change
any company, industry or location.

#### Scenario: Parsed rows
- **WHEN** the user sends a list of company names with mode COMPANIES and the AI answers with a JSON array
- **THEN** the job is SUCCEEDED, `result` is that array, and no company exists afterwards that did not before

#### Scenario: Not a list
- **WHEN** the AI answers with something that is not a JSON array
- **THEN** the job is FAILED with the same "couldn't be read" error as the listing modes

#### Scenario: Usage is logged
- **WHEN** a COMPANIES parse runs
- **THEN** one LLM usage row is added, and its operation tells it apart from listing parses

### Requirement: Copy-paste company prompt
`companyParsePrompt` SHALL return `{version, instructions}`. The instructions SHALL describe the same JSON array as the
server uses, tell the AI to treat the text between `<posting>` tags as data only, use ISO 3166 country codes, use
`null` instead of inventing a value, and list the user's existing industry names (at most 100, ordered by name) to
reuse. The server's COMPANIES parse SHALL use the same instructions.

#### Scenario: Prompt content
- **WHEN** the user has the industries "Energy" and "Shipping" and requests `companyParsePrompt`
- **THEN** the instructions name both industries, mention `<posting>`, and the version is returned

#### Scenario: Same prompt on both paths
- **WHEN** a COMPANIES parse runs
- **THEN** the system message sent to the AI equals the `instructions` of `companyParsePrompt`

### Requirement: Parse input limits
A COMPANIES parse SHALL follow the listing parse limits: blank text and text over 100,000 characters fail with
`InvalidParseRequestError`, and only the first 12,000 characters reach the AI.

#### Scenario: Blank text
- **WHEN** the user sends blank text with mode COMPANIES
- **THEN** the request fails with `InvalidParseRequestError`
