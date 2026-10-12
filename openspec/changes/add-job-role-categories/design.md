# Design

## Context

See proposal.md for motivation. JobRole is currently a user-owned, normalized-name lookup. ListingMutations finds or creates it from jobRoleName; there is no role management mutation. ListingQueries already applies GraphQL filters before offset paging. LookupSeeder runs on every startup. Full/quick prompts load the user's role names and return ListingInput-shaped JSON; URL parsing shares the full prompt. Parsing only returns results, with saves performed separately.

## Goals / Non-Goals

**Goals:** Dynamic category discovery, one category per role, AI suggestions within the existing parse call, corrections that survive later parsing, and compatibility with existing clients.

**Non-Goals:** Category hierarchies, multiple categories per role, per-listing category overrides, category deletion, independent classification jobs, automatic reclassification of stored roles, or edits to the sibling UI.

## Decisions

### Relational user-owned category lookup

Add JobRoleCategory (Id, UserId, Name, NormalizedName) under Features/JobRoles, with a unique (UserId, NormalizedName) index and global user filter. Add nullable CategoryId and Category navigation to JobRole, a CategoryId index, and a real FK using Restrict deletion. A nullable FK preserves old clients and honestly represents unknown classifications; no Other enum or required default. No duplicated category on Listing. Resolver validation ensures both ends belong to the current user, including updates and AI suggestions. An enum would require code deployments for new choices; a join table would add unnecessary multi-category behavior.

### GraphQL contract

- jobRoleCategories(search) uses cursor paging, projection/filter/sort, normalized case-insensitive search, and default name order like jobRoles. Clients must paginate to discover all choices.
- createJobRoleCategory(name) creates a trimmed name or returns the existing case-insensitive match. updateJobRoleCategory(id, name) renames and rejects another category's normalized name. Names must be nonblank and at most 100 characters.
- setJobRoleCategory(jobRoleId, categoryId: Int) assigns or explicitly clears. Expected errors: InvalidJobRoleCategoryError, JobRoleCategoryNotFoundError, DuplicateJobRoleCategoryError (existingCategoryId), JobRoleNotFoundError. Unknown and foreign IDs have the same not-found response.
- JobRole exposes categoryId/category. Existing nested listing filters support where: { jobRole: { categoryId: { eq: id } } }, in for multiple selections, and eq: null for uncategorized. Test nullable navigation, counts, and pagination in the actual generated schema rather than assuming translation works.

Category corrections apply to every listing using that role. A nullable assignment is intentional, not a listing-specific exception.

### AI suggests a category name; saving resolves it

Add optional jobRoleCategoryName to ListingInput and to full/quick prompt JSON. Names fit the existing name-based parse/save contract and external copy-paste workflow. Include all current user's categories in the prompt, plus existing role-category assignments alongside the existing capped role vocabulary. Instruct the model to reuse an existing assignment, otherwise choose an existing category from the role's duties, return null when uncertain, and never create a category. No additional AI call; companies parsing remains unchanged.

At save, a nonblank supplied name must resolve to the user's existing category using Names.Normalize; invalid/unknown values fail with InvalidListingError before any persisted changes, even if the role is already categorized. Suggestions initialize a newly created or uncategorized role; a categorized role stays unchanged. Omitted/null/blank input does not clear. Manual mutation is the explicit route for correcting or clearing. Thus manual clear permits a later suggestion to fill the role again; preserving a deliberate permanent opt-out is outside scope. This policy avoids adding provenance flags without a concrete need.

Parsing remains read-only with respect to categories and roles. Reuse existing result-shape validation; enforce category validity at save, as with other editable parse fields. Bump full version 9→10 and quick 5→6. Main specs contain historical fixed versions (4/2 and 5/3); code is authoritative for current baselines. The new delta specifies an increment from the immediately previous deployed version and does not rewrite historical unrelated requirements.

### Starter categories and safe initialization

Proposed starter vocabulary: IT, Healthcare, Education, Business & Administration, Sales & Marketing, Logistics, Trades & Manufacturing, Hospitality. It is editable user data, not an exhaustive taxonomy. Seed only missing categories. Known technical starter roles map to IT: Frontend Developer, Backend Developer, Full Stack Developer, Mobile Developer, DevOps Engineer, Site Reliability Engineer, Platform Engineer, Cloud Engineer, Data Engineer, Data Scientist, Machine Learning Engineer, QA Engineer, Security Engineer, Embedded Developer, Software Architect. Engineering Manager, UX Designer, Product Manager, and unrecognized stored roles remain uncategorized because their names are cross-industry.

Existing roles with those exact normalized technical names receive a one-time IT backfill in the migration. New starter roles created by LookupSeeder receive IT on creation only. Never reassign existing roles during startup, even when category is null; otherwise manual clearing would be undone. Startup seeding may restore missing starter category names after renaming, consistent with existing additive lookup seeding; document this limitation.

## Risks / Trade-offs

- [Shared role correction affects several listings] → Describe this in API documentation and expose the category through the role.
- [AI picks a valid but incorrect category] → Preview the result and provide manual correction; categorized roles are never overwritten by save.
- [Existing custom roles are invisible under IT until categorized] → Provide uncategorized filtering and manual assignment; no speculative AI backfill.
- [Categories grow and enlarge prompts] → Fetch the full vocabulary rather than silently making some valid choices unavailable; monitor prompt size through existing usage logging. No arbitrary new vocabulary cap in this change.
- [SQLite may rebuild JobRoles to add the FK] → Back up first and review generated Up for preservation of role IDs and listing references.
- [Concurrent first assignments] → Document ordinary last-write behavior for uncategorized roles; do not introduce reservation or scheduling machinery.

## Migration Plan

Use make migration name=AddJobRoleCategories, which backs up the real DB. Create the category table and optional role FK; insert missing starter categories per existing user and perform the one-time exact-name IT backfill using per-user category IDs. Review Up before applying, particularly any SQLite rebuild. Extend LookupSeeder for future accounts and new starter roles without recategorizing existing rows. Test upgrade on disposable SQLite data, retaining IDs and relationships. Rollback should use a fix-forward migration or a verified backup; dropping category data loses assignments, so do not delete the owner's database. Regenerate schema.graphql and document the UI integration contract before rollout.
