# Job role categories

The authenticated GraphQL API stores categories per user. Each job role has one optional category; every listing using that role inherits it. Company industry is independent: a bank's developer can have an IT role. Unknown roles remain uncategorized.

## Discover filter choices

Fetch every page of categories instead of hardcoding names or IDs:

```graphql
query Categories($after: String) {
  jobRoleCategories(first: 100, after: $after, order: [{ name: ASC }]) {
    nodes { id name }
    pageInfo { hasNextPage endCursor }
  }
}
```

Continue with `after: endCursor` while `hasNextPage` is true. `search` uses case-insensitive normalized matching (including Norwegian letters); `where` and `order` are supported. IDs are specific to the signed-in user. Category creation or renaming appears on the next fetch without a UI deployment.

```graphql
query ListingsByCategory($categoryId: Int!) {
  listings(where: { jobRole: { categoryId: { eq: $categoryId } } }) {
    items { id title jobRole { id name categoryId category { id name } } }
    totalCount
    pageInfo { hasNextPage }
  }
}
```

For multiple selected categories use `categoryId: { in: [1, 2] }` with the user's actual IDs. For uncategorized roles use `categoryId: { eq: null }`. Omit this filter to show all categories. Filters run before paging/counting and combine with existing search, archive, due/date/status filters and sorting.

## Manage categories and role assignments

```graphql
mutation CreateCategory($name: String!) {
  createJobRoleCategory(input: { name: $name }) {
    jobRoleCategory { id name }
    errors { __typename ... on Error { message } }
  }
}

mutation RenameCategory($id: Int!, $name: String!) {
  updateJobRoleCategory(input: { id: $id, name: $name }) {
    jobRoleCategory { id name }
    errors {
      __typename
      ... on Error { message }
      ... on DuplicateJobRoleCategoryError { existingCategoryId }
    }
  }
}

mutation AssignRoleCategory($jobRoleId: Int!, $categoryId: Int) {
  setJobRoleCategory(input: { jobRoleId: $jobRoleId, categoryId: $categoryId }) {
    jobRole { id name categoryId category { id name } }
    errors { __typename ... on Error { message } }
  }
}
```

Names are trimmed, must contain 1–100 characters, and are unique case-insensitively per user. Create reuses an existing match. Rename collisions return `DuplicateJobRoleCategoryError`; invalid names return `InvalidJobRoleCategoryError`. Unknown and foreign category/role IDs return `JobRoleCategoryNotFoundError`/`JobRoleNotFoundError`. No category delete operation is exposed.

Pass `categoryId: null` to clear an assignment. Correcting a role changes the category shown for **all listings with that role**. Refetch affected listings after correction. A clear survives restarts; a later parsed suggestion can fill an uncategorized role again.

## Parse, preview, then save

Full (version 10) and quick (version 6) listing prompts include existing categories and known role assignments. They ask for `jobRoleCategoryName` using an existing category name or null when uncertain. Full URL parsing shares the full prompt. Company parsing is unchanged. Category suggestions use the existing AI call; parsing returns JSON and does not write roles/categories.

Forward the optional `jobRoleCategoryName` from previewed JSON to `createListing` or `updateListing` alongside the normal listing fields. Saving resolves names case-insensitively against the user's existing categories, initializes new or uncategorized roles, and preserves any existing category. Omitted, null, or blank suggestions leave assignments unchanged. A nonblank unknown category name returns `InvalidListingError` without persisted changes, including when the role is already categorized. If a category was renamed between parsing and saving, refresh category choices and correct the suggestion before retrying.

Use `setJobRoleCategory` for explicit corrections; editing the suggestion alone will not override a categorized role. Existing clients and old saved parse results without the new field remain compatible. Concurrent first suggestions for an uncategorized role have ordinary last-write behavior.

The Nuxt UI needs one integration change: generate types from schema.graphql, fetch/paginate categories, populate the filter dynamically, send the nested filter, forward parse suggestions when saving, and expose category management/role correction as desired. This API repository does not implement those UI controls.

## Starter data and upgrade

Starter categories: IT, Healthcare, Education, Business & Administration, Sales & Marketing, Logistics, Trades & Manufacturing, Hospitality.

`AddJobRoleCategories` adds the category table and optional role FK, seeds categories for existing users, and assigns IT once to exact normalized technical starter roles: Frontend Developer, Backend Developer, Full Stack Developer, Mobile Developer, DevOps Engineer, Site Reliability Engineer, Platform Engineer, Cloud Engineer, Data Engineer, Data Scientist, Machine Learning Engineer, QA Engineer, Security Engineer, Embedded Developer, Software Architect. Existing IDs and listing relationships are preserved. Engineering Manager, UX Designer, Product Manager, and unknown/custom roles stay uncategorized; assign them manually or through a later parse/save suggestion.

Startup seeding adds missing categories and categorizes newly created technical starter roles only. Existing assignments and clears are preserved. Renaming a starter category can cause its original starter name to be added again on a later startup, consistent with the additive lookup seeder.

Migration generation backed up the owner database. The migration applies on the next normal API startup; tests use disposable databases. Review SQLite rebuild operations before deployment and use a fix-forward migration or verified backup for recovery.
