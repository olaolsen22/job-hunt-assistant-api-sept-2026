# Spec Delta

## Purpose

Turns a user's saved tailored CV draft into a PDF CV document attached to the listing, with text that applicant tracking systems can extract reliably.

## ADDED Requirements

### Requirement: Generate a CV document from the saved draft
The system SHALL render the saved tailored CV draft of one of the user's listings to a PDF and store it as a new unsent CV document on that listing, named like uploaded documents. Existing documents on the listing SHALL NOT change.

#### Scenario: Draft exists
- **WHEN** the user generates a CV document for a listing with a saved draft
- **THEN** a new CV document is returned and listed under that listing's documents
- **AND** its bytes are a PDF downloadable through the existing document download

#### Scenario: Generate twice
- **WHEN** the user generates a CV document again for the same listing
- **THEN** a second document is created and the first one is unchanged

#### Scenario: No saved draft
- **WHEN** the user generates a CV document for a listing without a saved draft
- **THEN** a typed error is returned and no document is created

#### Scenario: Another user's listing
- **WHEN** the user generates a CV document for another user's listing
- **THEN** a listing-not-found error is returned and no document is created

### Requirement: Draft content is rendered as extractable text
Every non-empty draft field SHALL appear in the PDF as extractable text, in the order contact, headline, summary, experience, education, certifications, projects, skills, languages, links. Within a section, entries and bullets SHALL keep the draft's order. Empty sections SHALL be omitted with their heading.

#### Scenario: Full draft round trip
- **WHEN** a draft with every section filled is rendered and its text extracted
- **THEN** each field value appears, sections appear in the stated order, and bullets keep their draft order

#### Scenario: Empty sections
- **WHEN** a draft has no projects or certifications
- **THEN** the extracted text contains no Projects or Certifications heading

### Requirement: ATS-safe text
The PDF SHALL use a standard single-column layout with English section headings (Summary, Experience, Education, Certifications, Projects, Skills, Languages, Links). Contact details SHALL be in the page body, not in page headers or footers. Dates SHALL be `MM/yyyy`, with an open end shown as `Present`. Skills SHALL be plain names without level graphics.

#### Scenario: Non-ASCII and ligature text
- **WHEN** a draft contains "Søknadsbehandler" and "efficient office"
- **THEN** the extracted text contains both strings exactly

#### Scenario: Contact details extract
- **WHEN** a draft has name, email, phone and location
- **THEN** each appears in the extracted text of page one before the first section heading

#### Scenario: Dates
- **WHEN** an experience runs from March 2021 with no end month
- **THEN** the extracted text contains "03/2021 – Present"
