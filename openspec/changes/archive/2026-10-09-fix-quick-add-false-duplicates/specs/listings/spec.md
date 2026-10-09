## MODIFIED Requirements

### Requirement: Possible duplicates on create
Creating a listing SHALL return the user's other listings at the same company with the same job role or the same
title (case-insensitive) as possible duplicates, newest first. A listing at a different company MUST NOT be returned,
whatever its title, role or position in a batch. Listings returned as previous attempts MUST NOT also be returned as
possible duplicates. The listing is saved either way.

#### Scenario: Same company and role, different URL
- **WHEN** listing A at company C with role R exists, and the user creates listing B at C with role R and another URL
- **THEN** B is saved and A is returned as a possible duplicate

#### Scenario: Previous attempt is not a possible duplicate
- **WHEN** REJECTED listing A at company C with role R has URL X, and the user creates listing B at C with role R and URL X
- **THEN** A is returned as a previous attempt and not as a possible duplicate

#### Scenario: Same title at different companies
- **WHEN** listing A "Senior Developer" at company C1 exists, and the user creates listing B "Senior Developer" at
  company C2 with another URL
- **THEN** B is saved and possible duplicates is empty

#### Scenario: Same title at different companies in one batch
- **WHEN** one request creates "Senior Developer" at C1 and then "Senior Developer" at C2 with different URLs
- **THEN** both are saved and neither payload returns the other as a possible duplicate
