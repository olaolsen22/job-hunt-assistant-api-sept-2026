# Spec Delta

## Purpose

Suggests better wording for the profile's own bullets, checked against the project's CV research and written in the
owner's style, without adding facts. Nothing changes until the owner applies the suggestions they accept.

## ADDED Requirements

### Requirement: Refinement scope
`refineProfileBullets` SHALL accept an optional `bulletId`, or an optional `kind` and `entryId` together. With
neither, every profile bullet SHALL be a target. With `bulletId`, only that bullet SHALL be a target. With `kind` and
`entryId`, only that entry's bullets SHALL be targets. The result SHALL contain suggestions for target bullets only.

#### Scenario: Whole profile
- **WHEN** the user calls `refineProfileBullets` with no scope arguments
- **THEN** any bullet on any entry can receive a suggestion

#### Scenario: One entry
- **WHEN** the user passes `kind: EXPERIENCE` and the id of an experience, and the AI also returns a suggestion for a
  bullet on another entry
- **THEN** the result holds suggestions only for that experience's bullets

#### Scenario: One bullet
- **WHEN** the user passes a `bulletId` and the AI returns suggestions for that bullet and a sibling bullet
- **THEN** the result holds only the suggestion for the requested bullet

### Requirement: Invalid scope
A scope SHALL fail with `InvalidProfileError` before any AI call when `bulletId` is combined with `kind` or
`entryId`, when only one of `kind` and `entryId` is given, or when the scope has no bullets. An unknown or another
user's bullet or entry SHALL fail with `ProfileEntryNotFoundError`.

#### Scenario: Mixed scope
- **WHEN** the user passes both `bulletId` and `entryId`
- **THEN** the request fails with `InvalidProfileError` and no AI call is made

#### Scenario: Half an entry scope
- **WHEN** the user passes `entryId` without `kind`
- **THEN** the request fails with `InvalidProfileError`

#### Scenario: Nothing to refine
- **WHEN** the user refines an entry that has no bullets
- **THEN** the request fails with `InvalidProfileError` and no AI call is made

#### Scenario: Foreign bullet
- **WHEN** the guest refines a bullet id that belongs to the owner
- **THEN** the request fails with `ProfileEntryNotFoundError` and the AI receives no data

### Requirement: Suggestion shape
A succeeded result SHALL list suggestions as `{bulletId, text, note}`. `text` SHALL be the full suggested bullet,
or null to keep the current text. `note` SHALL be a short hint to the user, or null. A suggestion with neither text
nor note, or whose text equals the current text, SHALL be left out. A failed AI call SHALL return status FAILED with
an error message and no suggestions.

#### Scenario: Already strong bullet
- **WHEN** the AI returns a bullet's current text unchanged and no note
- **THEN** the result holds no suggestion for that bullet

#### Scenario: Unreadable reply
- **WHEN** the AI's reply is not valid JSON
- **THEN** the result has status FAILED and an error message

### Requirement: Suggestions keep the source's facts
A suggested text SHALL NOT contain a number that the source bullet does not contain, nor a name from the user's
skill list that the source bullet does not mention. Facts from other bullets SHALL NOT count as the source. A text
that fails this check, is blank, or exceeds 500 characters SHALL be replaced by null, and the suggestion SHALL keep
its note.

#### Scenario: Added metric dropped
- **WHEN** the source bullet is "Built the booking flow in Vue" and the AI suggests "Built the booking flow in Vue,
  cutting drop-off by 20%"
- **THEN** the suggestion for that bullet has `text` null

#### Scenario: Added skill dropped
- **WHEN** the user's skill list holds "TypeScript", the source bullet doesn't mention it, and the suggestion adds it
- **THEN** the suggestion's `text` is null and its note, if any, is kept

#### Scenario: Reworded facts kept
- **WHEN** the source bullet is "Was responsible for making the checkout page work with keyboard" and the AI suggests
  "Made the checkout page fully usable by keyboard"
- **THEN** the suggestion keeps that text

#### Scenario: Sibling facts are not a source
- **WHEN** a sibling bullet mentions "React" and the suggestion for this bullet adds "React", which this bullet
  doesn't mention
- **THEN** the suggestion's `text` is null

### Requirement: Notes ask for missing facts
The instructions SHALL tell the AI to check each target bullet against the CV research: what was delivered, the
candidate's own part as distinct from the team's, the result or scope, active and concrete wording, technologies in
context, and no unexplained internal abbreviations. Where a bullet lacks a fact only the user knows, the AI SHALL
return a note asking for it rather than inventing it. Notes SHALL be in English and at most 300 characters; a longer
note SHALL be dropped.

#### Scenario: Missing result
- **WHEN** the prompt is built
- **THEN** its instructions ask for a note, not a made-up fact, when a bullet lacks the candidate's own part, the
  result, or the scale

#### Scenario: Note without new text
- **WHEN** the AI returns a note asking for the result and no text for a bullet
- **THEN** the result holds that suggestion with `text` null and the note

### Requirement: Owner's writing style
The data sent to the AI SHALL include the user's writing samples in the profile's content language, whole samples
best fit first within a fixed budget, and all profile bullets grouped by entry with the targets marked. The
instructions SHALL tell the AI to copy the candidate's voice from the samples and their own bullets, write in the
profile's content language, and avoid stock CV and AI phrasing.

#### Scenario: Samples in the content language
- **WHEN** the profile's content language is English and the user has an English and a Norwegian writing sample
- **THEN** the prompt holds the English sample and not the Norwegian one

#### Scenario: Other bullets as context
- **WHEN** the user refines one bullet
- **THEN** the prompt holds the other bullets too, marked as not to be refined

### Requirement: Prompt data stays CV content
The data sent to the AI SHALL hold only entry titles, employer, school or project names, kinds, dates, entry
skills, bullets, and writing samples. It SHALL NOT hold contact details, address, date of birth, nationality, work
authorization, links, references, or preferences.

#### Scenario: No personal details
- **WHEN** the profile has an email, phone and street address and the prompt is built
- **THEN** none of them appears in the prompt

### Requirement: Copy-paste prompt and preview
`profileBulletRefinementPrompt` SHALL take the same scope arguments and return `{version, instructions}` with the
same rules and data as the built-in call, asking for JSON only. `previewProfileBulletRefinement` SHALL take the
pasted reply and the same scope and return the same result shape, after the same scope, shape, and fact checks. Scope
errors on the query SHALL be top-level GraphQL errors.

#### Scenario: Same checks for pasted replies
- **WHEN** a pasted reply suggests a text with a number its source bullet lacks
- **THEN** the preview returns that suggestion with `text` null

#### Scenario: Pasted reply that isn't JSON
- **WHEN** the pasted reply is not valid JSON
- **THEN** the preview returns status FAILED with an error message

### Requirement: Apply accepted suggestions
`applyProfileBulletRefinement` SHALL accept a list of `{id, text}` and replace each bullet's text (trimmed, same
validation as `updateProfileBullet`) in one save, returning the profile. Ids, entries, and positions SHALL be
unchanged. The text SHALL NOT be fact-checked, so the user can add facts they know. An unknown or foreign id SHALL
fail with `ProfileEntryNotFoundError`, a duplicated id or invalid text with `InvalidProfileError`, and a failure SHALL
change nothing.

#### Scenario: Apply two bullets
- **WHEN** the user applies new texts for two bullets
- **THEN** both bullets have the new texts with the same ids and positions

#### Scenario: User adds a real metric
- **WHEN** the user edits a suggestion to add "by about 30%" and applies it
- **THEN** the bullet is saved with that text

#### Scenario: One bad id
- **WHEN** the user applies a list where one id belongs to another user
- **THEN** the request fails with `ProfileEntryNotFoundError` and no bullet changes

#### Scenario: Translations go stale
- **WHEN** a bullet with a Norwegian translation is changed by apply
- **THEN** that translation reports stale

### Requirement: AI operation safeguards
`refineProfileBullets` SHALL take optional `model` and `requestId`, run under the shared AI operation safeguards
(one active operation per user, safe duplicate responses), and log its LLM usage under the operation
`refine-profile-bullets`. It SHALL save nothing else.

#### Scenario: Usage logged
- **WHEN** a refinement call succeeds
- **THEN** one usage row with operation `refine-profile-bullets` exists and no bullet has changed

#### Scenario: Unknown model
- **WHEN** the user passes a model id outside the allowlist
- **THEN** the request fails with `UnknownLlmModelError`
