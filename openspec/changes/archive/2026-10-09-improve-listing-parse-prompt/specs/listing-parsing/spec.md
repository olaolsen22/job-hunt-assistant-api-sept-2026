# Spec Delta

## Purpose

Turns a pasted job ad into listing JSON with an LLM, through the copy-paste prompt or the server-side parse, and
defines what the prompts ask the model to extract.

## ADDED Requirements

### Requirement: Infer from signals, never invent
The full and quick listing prompts SHALL tell the model to infer fields from clear signals in the ad, to return null
only when the ad gives nothing to go on, and never to invent names, dates or URLs that aren't in the text.

#### Scenario: Prompt states the inference rule
- **WHEN** the full or quick listing prompt is requested
- **THEN** its instructions say to infer from clear signals and never to invent names, dates or URLs
- **AND** they no longer contain "Don't guess"

### Requirement: Formal job title
Both listing prompts SHALL define `title` as the formal job title: a "Stillingstittel"/"Job title" field when the
page has one, otherwise the ad's heading, never a slogan or full sentence.

#### Scenario: Ad with a headline and a Stillingstittel field
- **WHEN** an ad's headline is "Vi ser etter ein erfaren full-stack-utviklar…" and its Stillingstittel is
  "Full-stack-utviklar / Full-stack Developer"
- **THEN** the prompt's rules point the model at "Full-stack-utviklar / Full-stack Developer"

#### Scenario: Rule present in both prompts
- **WHEN** the full or quick listing prompt is requested
- **THEN** its instructions mention "Stillingstittel"

### Requirement: Seniority inferred from experience
The full listing prompt SHALL set seniority from an explicit word in the title or text first. Otherwise it SHALL
infer it from the experience asked for: 0–1 years or graduate → JUNIOR, 2–4 years → MID, 5+ years or
"erfaren"/"experienced" → SENIOR, leading people or a team → LEAD.

#### Scenario: Years and "erfaren" without a seniority word
- **WHEN** an ad asks for "ein erfaren full-stack-utviklar" with "minst fem års praktisk erfaring"
- **THEN** the prompt's rules map it to SENIOR

#### Scenario: Prompt lists the cutoffs
- **WHEN** the full listing prompt is requested
- **THEN** its instructions contain the JUNIOR, MID, SENIOR and LEAD cutoffs
- **AND** they no longer contain "Not from years alone"

### Requirement: Work mode rule
The full listing prompt SHALL map work mode from explicit wording only: home office or hybrid wording → HYBRID, fully
remote wording → REMOTE, explicit on-site wording → ONSITE. Flexible hours alone SHALL give null.

#### Scenario: Prompt defines workMode
- **WHEN** the full listing prompt is requested
- **THEN** its instructions contain a `workMode` rule with "hjemmekontor" → HYBRID and "fjernarbeid" → REMOTE

### Requirement: Relative deadlines
Both listing prompts SHALL map relative deadlines to a date: "i dag"/"today" → DATE with today's date,
"i morgen"/"tomorrow" → DATE with tomorrow's date.

#### Scenario: Apply by today
- **WHEN** an ad says "Søk senest i dag" and today is 2026-10-07
- **THEN** the prompt's rules give deadlineType DATE and deadlineOn 2026-10-07

### Requirement: Posting date only from publication
Both listing prompts SHALL fill `postedOn` only from a publication date. "Sist endret", "last modified" and
"oppdatert" dates SHALL NOT be used.

#### Scenario: Only a last-modified date
- **WHEN** the full or quick listing prompt is requested
- **THEN** its instructions say "Sist endret" is not postedOn

### Requirement: Skills only from the ad
The full listing prompt SHALL tell the model to list only skills whose names appear in the posting.

#### Scenario: Prompt forbids invented skills
- **WHEN** the full listing prompt is requested
- **THEN** its skills rule says only names that appear in the posting

### Requirement: Prompt versions bumped
A prompt text change SHALL raise its version: the full listing prompt to 5 and the quick listing prompt to 3.

#### Scenario: Versions after this change
- **WHEN** the full and quick listing prompts are requested
- **THEN** their versions are 5 and 3
