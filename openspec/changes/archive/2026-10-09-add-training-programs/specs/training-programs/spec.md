# Spec Delta

## Purpose

Marks a listing as a training program that leads to a job (academy, bootcamp), recording the end employer, program
dates, pay and job promise so programs are clearly distinguishable from normal jobs.

## ADDED Requirements

### Requirement: Optional training program on a listing
A listing SHALL have at most one training program with: `endCompany` (optional), `startsOn` and `endsOn` (optional
dates), `isPaid` (true, false or null for unknown) and `jobOffer` (GUARANTEED, ON_PASS, NONE, UNSPECIFIED). A listing
without one SHALL return `trainingProgram: null`.

#### Scenario: Listing with a program
- **WHEN** a listing is saved with a training program for end employer "Kongsberg Defence Communication", starting 2027-01-04, ending 2027-04-02, unpaid, ON_PASS
- **THEN** querying the listing returns those values in `trainingProgram`, with `endCompany.name` "Kongsberg Defence Communication"

#### Scenario: Normal listing
- **WHEN** a listing is saved without a training program
- **THEN** its `trainingProgram` is null

### Requirement: Listing company is the provider
When a training program is attached, the listing's `company` SHALL be the program provider and SHALL stay required;
the end employer MAY be absent when the ad doesn't name one.

#### Scenario: Unnamed end employer
- **WHEN** a listing is saved with company "AW Academy" and a training program without an end company
- **THEN** it is saved, `company.name` is "AW Academy" and `trainingProgram.endCompany` is null

### Requirement: End employer resolved like listing companies
The end employer SHALL be given by name and resolved to the user's existing company with the same normalized name,
or created as a new company for that user.

#### Scenario: Existing company reused
- **WHEN** the user already has company "Kongsberg Defence Communication" and saves a program with end company "kongsberg defence communication"
- **THEN** the program points at the existing company and no new company is created

### Requirement: Setting, keeping and removing a program
On create and update, an omitted `trainingProgram` input SHALL keep the current program (none on create), an explicit
`null` SHALL remove it, and a value SHALL replace it.

#### Scenario: Old client keeps the program
- **WHEN** a listing with a program is updated without the `trainingProgram` field
- **THEN** the program is unchanged

#### Scenario: Explicit removal
- **WHEN** a listing with a program is updated with `trainingProgram: null`
- **THEN** the listing has no program

### Requirement: Program dates validated
If both dates are given, `startsOn` MUST NOT be after `endsOn`; otherwise the mutation SHALL fail with a typed
validation error and change nothing.

#### Scenario: End before start
- **WHEN** a program is saved with startsOn 2027-04-02 and endsOn 2027-01-04
- **THEN** the mutation returns a validation error and the listing is unchanged

### Requirement: Filter listings by program
Listings SHALL be filterable to those with or without a training program.

#### Scenario: Only programs
- **WHEN** listings are queried filtered to those having a training program
- **THEN** only listings with a program are returned

### Requirement: Programs are private and deleted with their listing
A training program SHALL be visible only to the owner of its listing, and deleting the listing SHALL delete its
program. A company used as an end employer SHALL NOT be deletable while referenced, like a listing's company.

#### Scenario: Other user's program
- **WHEN** another user queries listings
- **THEN** they see neither the listing nor its program

#### Scenario: Listing deleted
- **WHEN** a listing with a program is deleted
- **THEN** its program is deleted too
