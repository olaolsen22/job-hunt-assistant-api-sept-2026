# Spec Delta

## ADDED Requirements

### Requirement: Extract training programs
The full listing prompt SHALL ask for `trainingProgram: { "endCompanyName", "startsOn", "endsOn", "isPaid",
"jobOffer" } | null`, set only when the ad is a training program or academy that leads to a job, and null for normal
jobs. The quick prompt SHALL NOT ask for it.

#### Scenario: Academy ad
- **WHEN** an ad describes a 12-week unpaid academy run by AW Academy, followed by employment at Kongsberg Defence Communication after passing the exam
- **THEN** the full result has `companyName` "AW Academy" and `trainingProgram` with `endCompanyName` "Kongsberg Defence Communication", `isPaid` false and `jobOffer` ON_PASS

#### Scenario: Normal job ad
- **WHEN** an ad is an ordinary job
- **THEN** `trainingProgram` is null

### Requirement: Program provider is the company
For training program ads, the full prompt SHALL use the provider as `companyName`, set `endCompanyName` only when the
ad names the employer (null for "partner companies"), and leave `isPaid` null unless the ad says whether the program
is paid.

#### Scenario: Vague employer
- **WHEN** an academy ad says graduates are hired by "one of our partner companies"
- **THEN** `endCompanyName` is null

### Requirement: Programs are not internships
The full prompt SHALL set `employmentType` for a training program ad to the type of the job after the program, or
null when unstated, never INTERNSHIP for the program itself.

#### Scenario: Program with unstated job type
- **WHEN** an academy ad doesn't say whether the job afterwards is permanent
- **THEN** `employmentType` is null

### Requirement: Full prompt version for training programs
The full listing prompt version SHALL be raised by one for this change; the quick prompt version SHALL stay the same.

#### Scenario: Version bumped
- **WHEN** the full listing prompt is requested after this change
- **THEN** its version is one higher than before the change
