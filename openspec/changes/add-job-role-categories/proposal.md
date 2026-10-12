# Proposal

## Why

Showing only IT jobs currently requires the UI to maintain a list of role IDs. Backend-managed categories let the UI discover filter choices dynamically, including newly added categories and roles.

## What Changes

- Add user-owned job role categories, queried and created/renamed through GraphQL.
- Give each role one optional category; allow manual assignment and clearing. Listings inherit the role's category.
- Filter listings by the role's category before pagination.
- Full and quick listing parses suggest an existing category; saving can categorize new or uncategorized roles without overwriting an existing assignment.
- Seed starter categories and categorize known technical starter roles; preserve existing assignments and leave unknown roles uncategorized.
- Keep existing clients compatible through optional input fields. No UI implementation, scheduler, separate AI classification endpoint, category deletion, or bulk AI backfill in this change.

## Capabilities

### New Capabilities

- `job-role-categories`: Discoverable categories, category management, role assignment, isolation, and starter data.

### Modified Capabilities

- `listings`: Category filtering and accepting category suggestions when saving listings.
- `listing-parsing`: Existing-category suggestions in full and quick parse prompts and results.

## Impact

EF Core entities/configurations and AppDbContext, LookupSeeder, JobRoles GraphQL, ListingMutations/queries, listing prompts, schema.graphql, and integration tests. Requires an additive SQLite migration and database backup. The sibling Nuxt UI will need one integration change to fetch categories, forward parse suggestions, and send the selected filter; subsequent category additions require no UI code changes. No new packages or provider calls.
