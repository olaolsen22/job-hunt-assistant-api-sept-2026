# Spec Delta

## MODIFIED Requirements

### Requirement: Formal job title
Both listing prompts SHALL define `title` as a short, direct job title in natural English: taken from a
"Stillingstittel"/"Job title" field when the page has one, otherwise the ad's heading, and translated to English
when the ad is in another language. The title SHALL keep seniority and specialty words and SHALL omit the company
name, location, workload/employment type, slogans, full sentences and duplicated bilingual variants. The prompts
SHALL NOT ask for the title "exactly as written" or "in the original language".

#### Scenario: Ad with a headline and a Stillingstittel field
- **WHEN** an ad's headline is "Vi ser etter ein erfaren full-stack-utviklar…" and its Stillingstittel is
  "Full-stack-utviklar / Full-stack Developer"
- **THEN** the prompt's rules point the model at the Stillingstittel field and ask for a short English title such
  as "Full-stack Developer"

#### Scenario: Norwegian title with extras
- **WHEN** an ad's title is "Senior systemutvikler .NET – Oslo, 100 % fast stilling"
- **THEN** the prompt's rules ask for a natural English title without location or employment details, such as
  "Senior .NET Developer"

#### Scenario: Rule present in both prompts
- **WHEN** the full or quick listing prompt is requested
- **THEN** both contain the short English title rule
- **AND** neither describes `title` as "exactly as written" or "original language"
