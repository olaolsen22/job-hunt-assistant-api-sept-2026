# Spec Delta

## ADDED Requirements

### Requirement: Guest account has demo data after seeding
After the guest account is created, the system SHALL seed it with demo data so
the guest sees a populated app on first login. The demo data seeding step runs
after account seeding and is idempotent.

#### Scenario: Guest signs in and sees data
- **WHEN** a guest signs in via the one-click guest endpoint after a fresh startup
- **THEN** the guest's listings, profile, and base CVs queries return seeded demo data
