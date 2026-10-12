# Spec Delta

## ADDED Requirements

### Requirement: Suggest only existing role categories
Full and quick listing prompts SHALL include the user's existing categories and known role assignments, and ask for jobRoleCategoryName per listing. The model SHALL reuse known assignments, otherwise select an existing category based on role duties, or return null when uncertain. It SHALL NOT invent category names or infer the role category solely from company industry.

#### Scenario: New technical role
- **WHEN** the full or quick prompt is requested with IT among the user's categories
- **THEN** it instructs the model to suggest IT for technical duties, using jobRoleCategoryName and only existing names

#### Scenario: Known role assignment
- **WHEN** an existing role has an assigned category
- **THEN** the prompt presents that assignment and instructs the model to reuse it

#### Scenario: No suitable category
- **WHEN** none of the supplied categories confidently fits the role
- **THEN** prompt instructions require null instead of inventing a name

### Requirement: Suggestions remain previewable results
Category suggestions SHALL be returned within existing full-object and quick-array parse results, including full URL parsing. Parsing SHALL NOT persist category or role changes. Saving remains a separate mutation; company parsing SHALL be unaffected.

#### Scenario: Parse before save
- **WHEN** a full or quick parse succeeds with a category suggestion
- **THEN** result JSON contains the suggestion and stored roles and categories remain unchanged

### Requirement: Category prompt version increments
The full and quick listing prompt versions SHALL each increase by one relative to their immediately previous deployed versions when category suggestions are introduced.

#### Scenario: Versions from current baseline
- **WHEN** category suggestions are introduced from full version 9 and quick version 5
- **THEN** the resulting versions are 10 and 6 respectively
