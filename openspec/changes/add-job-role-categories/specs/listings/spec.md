# Spec Delta

## ADDED Requirements

### Requirement: Category filtering before pagination
Listings SHALL be filterable through where.jobRole.categoryId using eq, in, and eq:null for uncategorized roles. Category filtering SHALL combine with search, archive state, due/date/status filters and sorting, and SHALL apply before paging and totalCount. Company industry SHALL NOT determine the role's category.

#### Scenario: IT jobs across industries
- **WHEN** the user filters by IT and a bank's developer has an IT role while an IT company's recruiter has another category
- **THEN** the developer is returned and the recruiter is excluded

#### Scenario: Paged matching results
- **WHEN** three of ten listings match the selected category and the client requests take:2
- **THEN** two matching listings are returned and totalCount is three

#### Scenario: Uncategorized filter
- **WHEN** the user filters categoryId with eq:null
- **THEN** only listings whose role has no category are returned

#### Scenario: Combined filters
- **WHEN** the user selects IT with search:"backend", status APPLIED, and archived:false
- **THEN** only active APPLIED backend matches with an IT role are returned

### Requirement: Saving category suggestions
ListingInput SHALL accept optional jobRoleCategoryName. A valid suggestion SHALL categorize a new or uncategorized role by resolving an existing owned category case-insensitively. An already categorized role SHALL retain its assignment. Omitted, null or blank suggestions SHALL leave assignments unchanged. Unknown nonblank suggestions SHALL fail with InvalidListingError and no persisted changes.

#### Scenario: Save new parsed role
- **WHEN** a listing is saved with a new role and jobRoleCategoryName:" it " matching the user's IT category
- **THEN** the role is created with IT and the listing references it

#### Scenario: Categorize existing unknown role
- **WHEN** a listing is saved for an uncategorized role with an existing category suggestion
- **THEN** the role receives that category and all listings using it inherit it

#### Scenario: Preserve correction
- **WHEN** a role already has Healthcare and a later save suggests IT
- **THEN** the listing saves and the role retains Healthcare

#### Scenario: Old clients remain compatible
- **WHEN** an existing client saves a listing without jobRoleCategoryName
- **THEN** saving succeeds and any existing assignment is preserved

#### Scenario: Unknown or foreign category name
- **WHEN** a nonblank suggestion matches no owned category, including a name owned only by another user
- **THEN** InvalidListingError is returned without creating categories, roles, or listings or changing stored data
