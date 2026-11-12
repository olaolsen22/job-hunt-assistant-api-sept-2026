# Spec Delta

## Purpose
Populates the guest account with a realistic demo dataset at startup so visitors
see a working app without creating data themselves.

## ADDED Requirements

### Requirement: Guest account is populated with demo data at startup
At startup, after accounts are seeded, the system SHALL populate the guest
account with demo data when it has no listings. The dataset SHALL include
companies, job listings in various application statuses, status changes, a CV
profile with experience and education entries and bullets, profile skills, a base
CV template with one version, and contacts with interactions.

#### Scenario: First startup seeds demo data
- **WHEN** the app starts and the guest account has zero listings
- **THEN** the guest account has at least 8 companies, 10 listings across
  multiple application statuses, a profile with experience and education entries,
  profile skills, a base CV with one version, and contacts with interactions

#### Scenario: Restart does not duplicate data
- **WHEN** the app restarts and the guest account already has listings
- **THEN** no new demo data is created and the existing data is unchanged

### Requirement: Demo data uses realistic job-market content
All seeded data SHALL use fictional but plausible names, titles, and descriptions
representative of Norwegian tech job-market roles (Oslo area). Company names,
contact names, and listing titles SHALL be clearly fictional to avoid confusion
with real entities.

#### Scenario: Listings span multiple statuses
- **WHEN** demo data is seeded
- **THEN** listings include at least one each in Saved, Applied, Interviewing,
  Rejected, and Offer status

#### Scenario: Profile has structured entries
- **WHEN** demo data is seeded
- **THEN** the profile has at least 2 experience entries with bullets, at least 1
  education entry, and at least 3 profile skills

### Requirement: Demo data respects entity constraints
Seeded demo data SHALL satisfy all existing entity constraints: required foreign
keys, unique indexes, normalized name fields, and user-owned UserId filters.

#### Scenario: All foreign keys are valid
- **WHEN** demo data is seeded
- **THEN** every listing references an existing company and job role, every
  contact references an existing company, and every profile entry references the
  guest's profile

### Requirement: Demo data does not affect other accounts
Seeding guest demo data SHALL NOT create, modify, or delete any data belonging to
the owner or extra accounts.

#### Scenario: Owner data untouched
- **WHEN** demo data is seeded for the guest
- **THEN** the owner's listings, profile, and contacts query returns only
  owner-created data
