# Tasks

## 1. Category model and starter data

- [x] 1.1 Add JobRoleCategory/configuration, optional indexed JobRole category FK/navigation, AppDbContext DbSet/global filter, and starter vocabulary/mapping; verify model tests cover name uniqueness per user, nullable assignment, and FK behavior.
- [x] 1.2 Generate AddJobRoleCategories with make migration name=AddJobRoleCategories (backup first), include per-user starter data and one-time exact-name IT backfill, and review Up; verify a disposable SQLite upgrade preserves role/listing IDs, references, and unknown/cross-industry roles.
- [x] 1.3 Extend LookupSeeder to create missing categories and categorize only newly created technical starter roles; extend LookupSeederTests for new users, repeated seeding, and corrections/clears surviving restart, and verify targeted tests pass.
- [x] 1.4 Document the category model, starter vocabulary, one-time backfill, and additive seed behavior in relevant docs/STATUS.md sections using log-progress; verify documentation matches the model and seeder.

## 2. Category management and listing filters

- [x] 2.1 Add paged searchable/filterable/sortable jobRoleCategories, create/rename mutations, and setJobRoleCategory with typed errors; add GraphQL integration tests for names, normalization, duplicates, missing/foreign IDs, discovery, and assigning/clearing shared roles; verify targeted tests pass.
- [x] 2.2 Expose categoryId/category on JobRole and enable nested listing filtering; add tests for eq/in/null, user isolation, pagination/totalCount, combined search/status/archive/due filters, and IT roles across company industries; verify generated-schema queries execute with expected results.
- [x] 2.3 Regenerate schema.graphql and document exact query/mutation examples plus category pagination and shared-role correction semantics in docs/job-role-categories.md; verify SchemaTests passes and documented operations match the schema.

## 3. Parse suggestions and saving

- [x] 3.1 Extend full/quick prompts with current owned categories, known role assignments, and optional jobRoleCategoryName; bump current versions by one (9→10 and 5→6); extend ListingParsePromptTests for both prompts, category privacy, uncertainty, and unchanged companies prompt; verify targeted tests pass.
- [x] 3.2 Add optional ListingInput.jobRoleCategoryName and resolve only existing owned categories before persisted changes, assigning only new/uncategorized roles; add listing tests for create/update, normalization, invalid suggestions without writes, omitted/null/blank input compatibility, preserving existing assignments, and manual-clear followed by recategorization; verify targeted tests pass.
- [x] 3.3 Extend FakeChatClient parse coverage for full/quick category results and shared full URL paths; verify results preserve suggestions without writing roles/categories and existing provider failure tests still pass.
- [x] 3.4 Refresh schema.graphql and update docs/job-role-categories.md plus relevant STATUS parsing sections with the preview/save flow, unknown-name errors, and one-time Nuxt integration requirements; verify SchemaTests passes and examples use actual schema fields.

## 4. Integration verification

- [x] 4.1 Run dotnet test once for the full suite after targeted checks, and openspec validate add-job-role-categories --strict; verify both pass without starting or stopping the owner's running API.
- [x] 4.2 Review the complete diff for additive client compatibility, migration safety, category isolation, and API-only scope; record the owner-requested rationale in .claude/commit-notes.md and verify no unrelated changes are included.
