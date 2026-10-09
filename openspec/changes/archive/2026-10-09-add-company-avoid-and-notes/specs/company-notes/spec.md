# Spec Delta

## Purpose

Lets the user keep one free-text Markdown note on a company: why to avoid it, or anything good or bad worth
remembering.

## ADDED Requirements

### Requirement: Set a company's notes
`setCompanyNotes` SHALL accept `{id, notes}` and return the company with `notes` replaced. Notes are Markdown, stored
as sent apart from trimming. An unknown id, or another user's company, SHALL fail with `CompanyNotFoundError`.

#### Scenario: Save notes
- **WHEN** the user sets notes `"  Avoid: three reorgs in a year  "` on a company
- **THEN** the returned company has notes `"Avoid: three reorgs in a year"`

#### Scenario: Replace notes
- **WHEN** the user sets notes on a company that already has notes
- **THEN** the returned company has only the new notes

#### Scenario: Unknown or foreign company
- **WHEN** the user sets notes on an id that does not exist for them
- **THEN** the request fails with `CompanyNotFoundError`

### Requirement: Blank notes clear
Null, empty or whitespace-only notes SHALL clear the company's notes to null.

#### Scenario: Clear with blank
- **WHEN** the user sets notes `"   "` on a company with notes
- **THEN** the returned company has `notes: null`

#### Scenario: Clear with null
- **WHEN** the user sets notes `null` on a company with notes
- **THEN** the returned company has `notes: null`

### Requirement: Notes length limit
Notes longer than 10,000 characters after trimming SHALL fail with `InvalidCompanyError`, and the company SHALL be
unchanged.

#### Scenario: Too long
- **WHEN** the user sets notes of 10,001 characters
- **THEN** the request fails with `InvalidCompanyError` and the stored notes are unchanged

### Requirement: Notes are not part of other edits
`updateCompany` and `createCompanies` SHALL NOT change notes. `setCompanyNotes` SHALL NOT change the company's name,
industry, locations, favorite or avoided state.

#### Scenario: Update keeps notes
- **WHEN** the user renames a company that has notes with `updateCompany`
- **THEN** its notes are unchanged

#### Scenario: Notes don't touch flags
- **WHEN** the user sets notes on an avoided favorite
- **THEN** `avoidedAt` and `favoritedAt` are unchanged
