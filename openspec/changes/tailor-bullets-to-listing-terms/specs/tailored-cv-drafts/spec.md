# Spec Delta

## ADDED Requirements

### Requirement: Tailored bullets may use the listing's wording
In a CV tailored to a listing, in either template, a source-attributed bullet SHALL be allowed to use the listing's terms where its source supports them. It SHALL keep its source's facts. It SHALL contain no number the source does not state and at most 2 words found in neither the source nor the listing. A bullet that breaks this SHALL fall back to its source text. The prompts SHALL ask for natural wording, not copied listing sentences.

#### Scenario: Detailed bullet adopts a listing term
- **WHEN** the listing mentions "cross-functional teams" and a Detailed analysis returns "Streamlined development process from start to finish within cross-functional product, design, and mobile teams, making it ~30% faster with a more consistent UI." for the source bullet "Streamlined development process from start to finish with product, design, and mobile teams, making it ~30% faster with a more consistent UI."
- **THEN** the suggestion keeps the tailored text and identifies its source bullet

#### Scenario: Concise bullet adopts a listing term
- **WHEN** a Concise analysis shortens a bullet and swaps in a listing phrase for work the bullet describes
- **THEN** the suggestion keeps the shortened, tailored text

#### Scenario: Invented number
- **WHEN** a tailored bullet states a number that only the listing has, such as "5 years" or "40%"
- **THEN** the bullet falls back to its source text

#### Scenario: Too many unsupported words
- **WHEN** a tailored bullet adds more than 2 words that appear in neither its source nor the listing
- **THEN** the bullet falls back to its source text

#### Scenario: Translated source
- **WHEN** the draft language has a translation of the source bullet
- **THEN** words from that translation count as source words

## MODIFIED Requirements

### Requirement: External source bullets preserve their source text
When an external candidate for a listing-tailored CV attributes a bullet to a profile source bullet, the candidate text SHALL either match that source bullet's text after the established whitespace normalization or pass the listing-wording check, in either template. A bullet that fails the check SHALL be replaced by its source text in the returned candidate rather than rejected. User-edited draft content saved through the draft save operation may differ from source text.

#### Scenario: Exact source bullet
- **WHEN** an external candidate includes a source bullet ID and the matching profile bullet text
- **THEN** the preview returns the candidate for review

#### Scenario: Bullet tailored to listing terms
- **WHEN** a Detailed external candidate includes a valid source bullet ID with text that uses the listing's terms and passes the listing-wording check
- **THEN** the preview returns the candidate with that tailored text

#### Scenario: Altered source bullet
- **WHEN** an external candidate includes a valid source bullet ID but text that fails the listing-wording check
- **THEN** the preview returns the candidate with that bullet's source text in place of the altered text

#### Scenario: User edits a source bullet in a draft
- **WHEN** a user saves draft text edited from a source bullet while retaining its source ID
- **THEN** the draft save accepts the edited text and preserves its source attribution
