# Spec Delta

## Purpose

Lets each user organize core job roles into editable categories that clients can discover dynamically and use to filter listings without hardcoded role lists.

## Requirements

### Requirement: Discoverable private categories
The API SHALL expose paginated jobRoleCategories with id/name, case-insensitive search, filtering, sorting, and default name ordering. Only the signed-in user's categories SHALL be visible.

#### Scenario: New category appears without client changes
- **WHEN** a user creates Healthcare and queries all pages of jobRoleCategories
- **THEN** Healthcare is returned as a selectable category with its ID

#### Scenario: User isolation
- **WHEN** another user queries categories or roles
- **THEN** the first user's categories and role assignments are not returned

### Requirement: Category creation and renaming
Users SHALL create and rename their categories through GraphQL. Names SHALL be trimmed, nonblank, at most 100 characters, and unique case-insensitively per user. Creation of an existing name SHALL return that category; rename collisions SHALL return DuplicateJobRoleCategoryError with existingCategoryId. Invalid names SHALL return InvalidJobRoleCategoryError.

#### Scenario: Case-insensitive create
- **WHEN** IT exists and its owner creates a category named " it "
- **THEN** the existing IT category is returned without creating another row

#### Scenario: Rename collision
- **WHEN** a user renames Healthcare to a name matching their existing IT category
- **THEN** DuplicateJobRoleCategoryError identifies IT and neither category changes

#### Scenario: Invalid names
- **WHEN** a user creates or renames a category with blank text or more than 100 characters
- **THEN** InvalidJobRoleCategoryError is returned without writes

### Requirement: One optional category per role
JobRole SHALL expose categoryId and category. setJobRoleCategory SHALL assign one existing owned category or clear it with null. Assignments SHALL be shared by all listings using the role. Unknown or foreign role/category IDs SHALL return JobRoleNotFoundError or JobRoleCategoryNotFoundError without writes.

#### Scenario: Correct an assignment
- **WHEN** a user assigns a role used by two listings to Healthcare
- **THEN** both listings expose Healthcare through their role

#### Scenario: Clear a category
- **WHEN** a user sets a role's categoryId to null
- **THEN** the role becomes uncategorized and its listings remain intact

#### Scenario: Foreign category rejected
- **WHEN** a user assigns their role to another user's category ID
- **THEN** JobRoleCategoryNotFoundError is returned and the role is unchanged

#### Scenario: Foreign role rejected
- **WHEN** a user assigns a category to another user's role ID
- **THEN** JobRoleNotFoundError is returned without writes

### Requirement: Starter categories and upgrade preservation
Each user SHALL receive starter categories IT, Healthcare, Education, Business & Administration, Sales & Marketing, Logistics, Trades & Manufacturing, and Hospitality. Upgrade SHALL categorize recognized technical starter roles as IT once, preserving role/listing IDs. Unknown and cross-industry roles SHALL remain uncategorized. Later startup seeding SHALL preserve existing assignments including null.

#### Scenario: Existing technical role upgraded
- **WHEN** the upgrade encounters Backend Developer and Warehouse Worker for an existing user
- **THEN** Backend Developer receives that user's IT category and Warehouse Worker stays uncategorized
- **AND** their role IDs and linked listings are preserved

#### Scenario: Correction survives restart
- **WHEN** a user changes or clears a role's category and the API restarts
- **THEN** the assignment stays as the user set it

#### Scenario: New account
- **WHEN** lookups are seeded for a new user
- **THEN** starter categories are created and recognized technical starter roles receive that user's IT category
