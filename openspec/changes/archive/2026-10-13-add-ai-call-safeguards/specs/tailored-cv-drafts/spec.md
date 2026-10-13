# Spec Delta

## MODIFIED Requirements

### Requirement: Analyze profile content for a listing
The system SHALL analyze a user's profile content in relation to one of the user's listings, either for a requested section or across the profile as a whole. Analysis SHALL return suggestions for review and SHALL NOT modify the profile or save a CV draft. Full-profile analysis SHALL stop sending requests after a systemic provider/credential failure or application limit rejection, preserve completed results, and identify remaining sections as SKIPPED with a safe reason.

#### Scenario: Analyze one experience section
- **WHEN** the user requests analysis for one profile experience on a listing
- **THEN** the result identifies relevant bullets from that experience in relation to the listing
- **AND** no other profile entry is changed

#### Scenario: Analyze the full profile
- **WHEN** the user requests analysis across the profile for a listing
- **THEN** the result contains suggestions for relevant profile sections in relation to that listing
- **AND** no profile entry or saved draft is changed

#### Scenario: Listing belongs to another user
- **WHEN** the user requests analysis for another user's listing
- **THEN** the request is rejected without disclosing listing or profile content

#### Scenario: Invalid credential during batch
- **WHEN** a section attempt is rejected with invalid-key or credential-unavailable
- **THEN** that section fails and all remaining sections are SKIPPED without provider calls or inference usage

#### Scenario: Rate limiting or provider outage
- **WHEN** a section fails with rate-limited, timeout, or provider-error
- **THEN** its result is FAILED and later sections are SKIPPED with the same safe error code
- **AND** prior successful suggestions are retained

#### Scenario: Personal daily limit reached
- **WHEN** the next section cannot be admitted because the user's personal-provider daily limit is exhausted
- **THEN** that section and later sections are SKIPPED with daily-limit-reached
- **AND** no attempt or usage is created for skipped sections

#### Scenario: Invalid section output
- **WHEN** one admitted section returns invalid-json while its credential and provider remain available
- **THEN** that section fails and later sections can still be analyzed subject to the operation's guards

#### Scenario: Caller cancellation
- **WHEN** the caller cancels full-profile analysis
- **THEN** cancellation propagates without submitting further sections or automatically retrying the operation
