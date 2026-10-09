# Spec Delta

## ADDED Requirements

### Requirement: Apply with optional documents
Applying SHALL move a listing from SAVED to APPLIED whether or not a CV or cover letter is given. Each given document
MUST belong to the listing and be of the matching kind, and is marked sent on the applied date. Documents that are
not given are not marked sent.

#### Scenario: Apply without a CV
- **WHEN** the user applies to a SAVED listing with no CV and no cover letter, with a past `appliedAt` and a note
- **THEN** the listing's status is APPLIED, its timeline ends with an APPLIED step on that date with that note, and none of its documents is sent

#### Scenario: Apply with a CV
- **WHEN** the user applies to a SAVED listing with a CV of that listing
- **THEN** the listing's status is APPLIED and the CV is sent on the applied date

#### Scenario: Wrong kind still rejected
- **WHEN** the user applies with a cover letter's id as the CV
- **THEN** the request fails with `InvalidDocumentError` and the listing stays SAVED

#### Scenario: Undo an application without documents
- **WHEN** the user undoes on a listing whose only step is an APPLIED step with no sent documents
- **THEN** the listing's status is SAVED
