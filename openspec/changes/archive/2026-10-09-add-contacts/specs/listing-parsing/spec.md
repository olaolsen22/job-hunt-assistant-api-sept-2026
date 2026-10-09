# Spec Delta

## ADDED Requirements

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
