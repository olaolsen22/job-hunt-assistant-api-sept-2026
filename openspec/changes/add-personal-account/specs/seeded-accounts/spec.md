# Spec Delta

## Purpose

Creates the app's user accounts from configuration at startup, since registration is closed. Covers the owner, the
guest, and extra password accounts, and keeps each account's password in sync with its config.

## ADDED Requirements

### Requirement: Extra accounts are seeded from configuration
At startup the system SHALL create a password account with the Owner role for each configured extra user
(`Seed:Users:N:Email`, `Seed:Users:N:Password`) that does not exist yet. Seeded addresses count as confirmed.

#### Scenario: Configured user signs in
- **WHEN** `Seed:Users:0` has an email and password and the app starts
- **THEN** `POST /api/auth/login` with that email and password succeeds, and `GET /api/auth/me` returns that email with role `Owner`

#### Scenario: No extra users configured
- **WHEN** there are no `Seed:Users` entries
- **THEN** startup succeeds and only the owner and guest accounts exist

### Requirement: Seeding never alters other accounts
Seeding an extra user SHALL NOT change any other account, its password, or its data. An extra user that already exists
SHALL keep its id and data.

#### Scenario: Owner untouched
- **WHEN** an extra user is configured alongside the owner
- **THEN** the owner still signs in with `Seed:OwnerPassword`

#### Scenario: Restart keeps the account
- **WHEN** the app restarts with the same `Seed:Users` entry
- **THEN** no duplicate account is created and the user's data is still there

### Requirement: Extra user passwords follow configuration
On each startup the system SHALL set an extra user's password to the configured value when it differs, the same way as
the owner's.

#### Scenario: Password changed in config
- **WHEN** `Seed:Users:0:Password` changes and the app restarts
- **THEN** login works with the new password and fails with the old one

### Requirement: Accounts see only their own data
An extra user SHALL see only data it created, and SHALL NOT see the owner's or guest's data.

#### Scenario: Separate listings
- **WHEN** the owner creates a listing and the extra user queries `listings`
- **THEN** the owner's listing is not returned
