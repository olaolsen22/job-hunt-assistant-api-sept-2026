# applications Specification

## Purpose
Tracks one attempt at a job ad from saved to its outcome: the status a listing is in, which steps are allowed, the
timeline of steps, and when an attempt is over.

## Requirements

### Requirement: Status transitions
A listing's status SHALL change only along these transitions: SAVED → APPLIED; APPLIED → INTERVIEWING, OFFER,
REJECTED or WITHDRAWN; INTERVIEWING → OFFER, REJECTED or WITHDRAWN; OFFER → ACCEPTED, REJECTED or WITHDRAWN. Any other
transition MUST fail with `InvalidStatusTransitionError` and leave the listing unchanged.

#### Scenario: Accept an offer
- **WHEN** the user records outcome ACCEPTED on a listing with status OFFER
- **THEN** the listing's status is ACCEPTED and its timeline ends with an ACCEPTED step

#### Scenario: Company withdraws an offer
- **WHEN** the user records outcome REJECTED on a listing with status OFFER
- **THEN** the listing's status is REJECTED

#### Scenario: Decline an offer
- **WHEN** the user records outcome WITHDRAWN on a listing with status OFFER
- **THEN** the listing's status is WITHDRAWN

#### Scenario: Accept without an offer
- **WHEN** the user records outcome ACCEPTED on a listing with status APPLIED or INTERVIEWING
- **THEN** the request fails with `InvalidStatusTransitionError`

### Requirement: Final statuses
REJECTED, WITHDRAWN and ACCEPTED SHALL be final: no transition leads out of them. Only undo can leave a final status.

#### Scenario: Outcome after acceptance
- **WHEN** the user records any outcome on a listing with status ACCEPTED
- **THEN** the request fails with `InvalidStatusTransitionError`

#### Scenario: Interview after a final status
- **WHEN** the user adds an interview to a listing with status REJECTED, WITHDRAWN or ACCEPTED
- **THEN** the request fails with `InvalidStatusTransitionError`

### Requirement: Closed listings
A listing SHALL be closed exactly when its status is final (REJECTED, WITHDRAWN or ACCEPTED), and open otherwise.
Closed is derived from status. Archiving MUST NOT open or close a listing.

#### Scenario: Archived saved listing stays open
- **WHEN** a listing with status SAVED is archived
- **THEN** it is still open

#### Scenario: Unarchived rejected listing stays closed
- **WHEN** a listing with status REJECTED is unarchived
- **THEN** it is still closed

### Requirement: Undo the latest step
Undo SHALL remove the latest step from a listing's timeline and restore the status before it (SAVED when no step is
left). It MUST fail with `InvalidStatusTransitionError` when there is nothing to undo, or when the latest step is
INTERVIEWING and the listing still has interviews.

#### Scenario: Undo an acceptance
- **WHEN** the user undoes on a listing whose latest step is ACCEPTED
- **THEN** the listing's status is OFFER

### Requirement: Undo never reopens a duplicate URL
Undo that would make a closed listing open SHALL fail with `DuplicateListingError` carrying the id of the open
listing, when another open listing of the same user has the same normalized URL. The listing MUST stay unchanged.

#### Scenario: Newer attempt exists
- **WHEN** listing A (REJECTED) and listing B (SAVED) have the same URL, and the user undoes on A
- **THEN** the request fails with `DuplicateListingError` whose `existingListingId` is B, and A stays REJECTED

#### Scenario: No newer attempt
- **WHEN** listing A (REJECTED) is the only listing with its URL, and the user undoes on A
- **THEN** A returns to its previous status

#### Scenario: Newer attempt is closed too
- **WHEN** listing A (REJECTED) and listing B (WITHDRAWN) have the same URL, and the user undoes on A
- **THEN** A returns to its previous status

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
