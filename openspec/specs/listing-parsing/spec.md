# listing-parsing Specification

## Purpose
Turns a pasted job ad into listing JSON with an LLM, reports every outcome on a ParseJob the user can read, and logs
every provider call for usage tracking.

## Requirements

### Requirement: Parse outcome
`parseListing` SHALL return a ParseJob that is SUCCEEDED with `result` JSON (an object for FULL, an array for QUICK)
or FAILED with a readable `error`. Blank text or text over 100,000 characters MUST fail with
`InvalidParseRequestError` and create no ParseJob.

#### Scenario: Successful full parse
- **WHEN** the provider answers with a JSON object for a FULL parse
- **THEN** the ParseJob is SUCCEEDED and `result` is that JSON

#### Scenario: Wrong JSON shape
- **WHEN** the provider answers with an object for a QUICK parse
- **THEN** the ParseJob is FAILED and `result` is null

### Requirement: Provider failures fail the job
A provider failure SHALL mark the ParseJob FAILED with a readable error instead of failing the request: rate limit
("free-tier limit"), unreachable or erroring provider ("couldn't be reached"), and timeout ("took too long").

#### Scenario: Rate limited
- **WHEN** the provider rejects the call with HTTP 429
- **THEN** the ParseJob is FAILED with an error mentioning the free-tier limit

#### Scenario: Timeout
- **WHEN** the provider call times out
- **THEN** the ParseJob is FAILED with an error saying the AI took too long, and the ParseJob is saved

### Requirement: Every provider call is logged
Each provider call SHALL write one usage row with provider, model, operation, latency and success, plus tokens when the
provider reports them and an error code on failure: `invalid-json`, `rate-limited`, `provider-error` or `timeout`.

#### Scenario: Usage after success
- **WHEN** a parse succeeds and the provider reports 1200 input and 300 output tokens
- **THEN** a successful usage row with those token counts is saved

#### Scenario: Usage after timeout
- **WHEN** the provider call times out
- **THEN** a failed usage row with error code `timeout` is saved

### Requirement: Parse jobs are private
A ParseJob SHALL be readable only by the user who created it; `parseJob(id)` for another user's job MUST return null.

#### Scenario: Other user's job
- **WHEN** the guest queries the owner's ParseJob id
- **THEN** the result is null

### Requirement: Infer from signals, never invent
The full and quick listing prompts SHALL tell the model to infer fields from clear signals in the ad, to return null
only when the ad gives nothing to go on, and never to invent names, dates or URLs that aren't in the text.

#### Scenario: Prompt states the inference rule
- **WHEN** the full or quick listing prompt is requested
- **THEN** its instructions say to infer from clear signals and never to invent names, dates or URLs
- **AND** they no longer contain "Don't guess"

### Requirement: Formal job title
Both listing prompts SHALL define `title` as a short, direct job title in natural English: taken from a
"Stillingstittel"/"Job title" field when the page has one, otherwise the ad's heading, and translated to English
when the ad is in another language. The title SHALL keep seniority and specialty words and SHALL omit the company
name, location, workload/employment type, slogans, full sentences and duplicated bilingual variants. The prompts
SHALL NOT ask for the title "exactly as written" or "in the original language".

#### Scenario: Ad with a headline and a Stillingstittel field
- **WHEN** an ad's headline is "Vi ser etter ein erfaren full-stack-utviklar…" and its Stillingstittel is
  "Full-stack-utviklar / Full-stack Developer"
- **THEN** the prompt's rules point the model at the Stillingstittel field and ask for a short English title such
  as "Full-stack Developer"

#### Scenario: Norwegian title with extras
- **WHEN** an ad's title is "Senior systemutvikler .NET – Oslo, 100 % fast stilling"
- **THEN** the prompt's rules ask for a natural English title without location or employment details, such as
  "Senior .NET Developer"

#### Scenario: Rule present in both prompts
- **WHEN** the full or quick listing prompt is requested
- **THEN** both contain the short English title rule
- **AND** neither describes `title` as "exactly as written" or "original language"

### Requirement: Seniority inferred from experience
The full listing prompt SHALL set seniority from an explicit word in the title or text first. Otherwise it SHALL
infer it from the experience asked for: 0–1 years or graduate → JUNIOR, 2–4 years → MID, 5+ years or
"erfaren"/"experienced" → SENIOR, leading people or a team → LEAD.

#### Scenario: Years and "erfaren" without a seniority word
- **WHEN** an ad asks for "ein erfaren full-stack-utviklar" with "minst fem års praktisk erfaring"
- **THEN** the prompt's rules map it to SENIOR

#### Scenario: Prompt lists the cutoffs
- **WHEN** the full listing prompt is requested
- **THEN** its instructions contain the JUNIOR, MID, SENIOR and LEAD cutoffs
- **AND** they no longer contain "Not from years alone"

### Requirement: Work mode rule
The full listing prompt SHALL map work mode from explicit wording only: home office or hybrid wording → HYBRID, fully
remote wording → REMOTE, explicit on-site wording → ONSITE. Flexible hours alone SHALL give null.

#### Scenario: Prompt defines workMode
- **WHEN** the full listing prompt is requested
- **THEN** its instructions contain a `workMode` rule with "hjemmekontor" → HYBRID and "fjernarbeid" → REMOTE

### Requirement: Relative deadlines
Both listing prompts SHALL map relative deadlines to a date: "i dag"/"today" → DATE with today's date,
"i morgen"/"tomorrow" → DATE with tomorrow's date.

#### Scenario: Apply by today
- **WHEN** an ad says "Søk senest i dag" and today is 2026-10-07
- **THEN** the prompt's rules give deadlineType DATE and deadlineOn 2026-10-07

### Requirement: Posting date only from publication
Both listing prompts SHALL fill `postedOn` only from a publication date. "Sist endret", "last modified" and
"oppdatert" dates SHALL NOT be used.

#### Scenario: Only a last-modified date
- **WHEN** the full or quick listing prompt is requested
- **THEN** its instructions say "Sist endret" is not postedOn

### Requirement: Skills only from the ad
The full listing prompt SHALL tell the model to list only skills whose names appear in the posting.

#### Scenario: Prompt forbids invented skills
- **WHEN** the full listing prompt is requested
- **THEN** its skills rule says only names that appear in the posting

### Requirement: Prompt versions bumped
A prompt text change SHALL raise its version: the full listing prompt to 5 and the quick listing prompt to 3.

#### Scenario: Versions after this change
- **WHEN** the full and quick listing prompts are requested
- **THEN** their versions are 5 and 3

### Requirement: Extract contact people
The full listing prompt SHALL ask for `contacts: [{ "name", "title", "email", "phone", "url" }]` containing only
people the ad names as contacts, with fields copied from the text and null when absent, and `[]` when none. Generic
inboxes without a person ("jobb@acme.no") SHALL be skipped. The quick prompt SHALL NOT ask for contacts.

#### Scenario: Ad names a contact
- **WHEN** the full prompt is requested
- **THEN** its JSON shape includes `contacts` and its rules say to never invent contact details

#### Scenario: Quick prompt unchanged
- **WHEN** the quick prompt is requested
- **THEN** its instructions do not mention `contacts`

#### Scenario: Server-side parse returns contacts
- **WHEN** a full parse job completes and the model returned a contact
- **THEN** the parse result JSON carries it so the UI can pass it into `ListingInput.contacts`
