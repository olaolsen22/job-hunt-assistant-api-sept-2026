# user-profile Specification

## Purpose
One master profile per user: who they are (basics) and what they're looking for (preferences). History, skills, and
languages hang off the same profile (see `profile-history`, `profile-skills`).

## Requirements

### Requirement: Every user has exactly one profile
Each user SHALL have exactly one profile, which exists from the time the user exists. `profile` SHALL return the
signed-in user's profile and never null. A user SHALL never see or change another user's profile or its entries.

#### Scenario: New user gets an empty profile
- **WHEN** a user who never saved anything queries `profile`
- **THEN** a profile is returned with all basics and preferences null or empty, and empty entry lists

#### Scenario: Profiles are per user
- **WHEN** the owner has saved a full name and the guest queries `profile`
- **THEN** the guest's profile does not show the owner's full name

### Requirement: Update profile basics
`updateProfile` SHALL accept `{fullName, headline, summary, email, phone, location, links}` and fully replace these
fields, returning the profile. Strings SHALL be trimmed, and blank strings stored as null. It SHALL NOT change
preferences, entries, skills, or languages.

#### Scenario: Save basics
- **WHEN** the user saves full name `" Ola Nordmann "` and headline `"Frontend engineer"`
- **THEN** the returned profile has fullName `"Ola Nordmann"` and headline `"Frontend engineer"`

#### Scenario: Omitted field clears
- **WHEN** the user saves basics without `phone` on a profile that has a phone
- **THEN** the returned profile has `phone: null`

#### Scenario: Basics don't touch entries
- **WHEN** the user with two experience entries saves basics
- **THEN** the profile still has both experience entries

### Requirement: Basics validation
Name, headline, and phone SHALL be at most 200 characters, and the summary (Markdown) at most 10,000. The email SHALL
be a valid address. Links SHALL number at most 10, each with a label of at most 50 characters and an absolute http/https
URL. Any violation SHALL fail with `InvalidProfileError` and leave the profile unchanged.

#### Scenario: Bad email
- **WHEN** the user saves email `"not-an-email"`
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Non-http link
- **WHEN** the user saves a link with URL `"ftp://example.com"`
- **THEN** the request fails with `InvalidProfileError`

### Requirement: Home location uses the location lookup
The profile `location` input SHALL take `{city?, countryCode}`. The location SHALL be found or created in the user's
locations, with the same rules as listing locations, and null SHALL clear it. Setting it SHALL NOT change the
location's `isTarget`.

#### Scenario: Existing location reused
- **WHEN** the user saves location `{city: "oslo", countryCode: "NO"}` and Oslo, NO already exists
- **THEN** the profile's location is the existing Oslo row, and no new location is created

#### Scenario: Invalid country
- **WHEN** the user saves location `{countryCode: "XX"}`
- **THEN** the request fails with `InvalidLocationError`

### Requirement: Update job preferences
`updateProfilePreferences` SHALL accept `{jobRoleNames, workModes, employmentTypes, minSalary, salaryCurrency,
noticePeriodMonths}`, fully replace these fields, and return the profile. Job roles SHALL be found or created by name
in the user's job roles. Repeated roles, work modes, or employment types SHALL collapse to one.

#### Scenario: Save preferences
- **WHEN** the user saves job roles `["Frontend Developer", "frontend developer", "Fullstack Developer"]` and work
  modes `[REMOTE, HYBRID]`
- **THEN** the profile has two target job roles and work modes REMOTE and HYBRID

#### Scenario: New role created
- **WHEN** the user saves a job role name that doesn't exist yet
- **THEN** it appears in `jobRoles` with `listingCount: 0`

### Requirement: Preferences validation
`minSalary` SHALL be zero or more. `salaryCurrency` SHALL be an ISO 4217 code, stored uppercase, and SHALL be required
when `minSalary` is set. `noticePeriodMonths` SHALL be 0–12. Any violation SHALL fail with `InvalidProfileError`.

#### Scenario: Salary without currency
- **WHEN** the user saves `minSalary: 700000` and no currency
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Currency normalized
- **WHEN** the user saves `minSalary: 700000, salaryCurrency: "nok"`
- **THEN** the profile has `salaryCurrency: "NOK"`

### Requirement: Target locations come from the location lookup
The profile SHALL expose `targetLocations`: the user's locations with `isTarget: true`. They SHALL be changed only
through the existing `saveLocation`, not through profile mutations.

#### Scenario: Target location shows on profile
- **WHEN** the user calls `saveLocation` with Bergen, NO and `isTarget: true`
- **THEN** `profile.targetLocations` includes Bergen
