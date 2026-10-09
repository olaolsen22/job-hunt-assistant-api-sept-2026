# contacts Specification

## Purpose
Contact people at companies, and which of them a listing names.

## Requirements

### Requirement: Contacts belong to a company
The system SHALL store contacts per user and company with a required name and optional title, email, phone, url and
notes. A contact name SHALL be unique per company, compared normalized (same rule as company names).

#### Scenario: Add a contact
- **WHEN** the user adds contact "Kari Nordmann", email "kari@acme.no" to company Acme
- **THEN** `company.contacts` for Acme returns Kari with that email

#### Scenario: Duplicate name in the same company
- **WHEN** the user adds "kari nordmann" to Acme, which already has "Kari Nordmann"
- **THEN** the mutation returns a typed duplicate error with the existing contact's id

#### Scenario: Other users can't see it
- **WHEN** another user queries companies or contacts
- **THEN** Kari is not returned

### Requirement: Edit and delete contacts
The system SHALL let the user update all contact fields and delete a contact. Deleting a contact SHALL unlink it from
listings without deleting them. Deleting a company SHALL delete its contacts.

#### Scenario: Delete a linked contact
- **WHEN** the user deletes a contact linked to two listings
- **THEN** both listings remain and their `contacts` no longer include it

### Requirement: Listings link to contacts
`ListingInput` SHALL accept a `contacts` list. On create/update the system SHALL match each entry by normalized name
within the listing's company, create missing ones, fill empty fields on matched ones without overwriting set fields,
and replace the listing's links with exactly those contacts.

#### Scenario: Parsed contact is new
- **WHEN** a listing for Acme is saved with contacts `[{ name: "Ola Hansen", phone: "900 00 000" }]`
- **THEN** Acme gains contact Ola Hansen and `listing.contacts` returns him

#### Scenario: Existing contact is reused
- **WHEN** a second Acme listing is saved with `[{ name: "ola hansen", email: "ola@acme.no" }]`
- **THEN** no new contact is created, Ola keeps his phone and gains the email, and both listings link to him

#### Scenario: Empty list unlinks
- **WHEN** a listing is updated with `contacts: []`
- **THEN** it has no linked contacts and the company's contacts are unchanged

#### Scenario: Omitted contacts keep links
- **WHEN** a listing is updated without a `contacts` field (null)
- **THEN** its existing links are unchanged, so clients unaware of contacts don't wipe them
