# Tasks

## 1. Industry and locations on companies

- [x] 1.1 Add `Industry` (`Features/Industries`, modelled on `JobRole`: unique `(UserId, NormalizedName)`, query filter, `DbSet`), `Company.IndustryId?`/`Industry` (SetNull) and `CompanyLocation` (composite key, both sides cascade), and remove `Company.LocationId`/`Location`; verify `dotnet build` passes
- [x] 1.2 Generate `make migration name=AddCompanyIndustryAndLocations`, add `INSERT INTO CompanyLocation ... SELECT Id, LocationId FROM Companies WHERE LocationId IS NOT NULL` in `Up` after the table is created and before the column is dropped; verify the migration's `Up` has the `INSERT` in that order and `dotnet test` passes
- [x] 1.3 Change `updateCompany` to `{id, name, industryName?, locations}` (full replace, shared private helper in `CompanyMutations` that resolves the industry and locations), keeping the `InvalidCompanyError`/`CompanyNotFoundError`/`InvalidLocationError` behavior; verify new tests: replace locations, clear industry and locations, rename onto an existing name fails and leaves the company unchanged, unknown id
- [x] 1.4 Expose `Company.industry`/`locations`, make `companies` filterable by industry, add `industries(search)` ordered by name; update existing tests that used `Company.location` or `updateCompany(location)`; verify new tests: filter by industry, `industries(search: "ene")` is case-insensitive and ordered, another user's industries are invisible
- [x] 1.5 Regenerate `schema.graphql`, update `docs/STATUS.md` (entities, `Industry`, `CompanyLocation`, `updateCompany`, BREAKING note for the UI) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS describes the breaking change

## 2. Bulk create

- [x] 2.1 Add `createCompanies(input:{companies})` → `[{company, created}]`: validate batch size (1–100), blank names and country codes first, then build rows with per-batch dictionaries for companies, industries and locations, and a single `SaveChangesAsync`; verify new tests: several new companies in order, existing name (different case, æøå) returns `created: false` unchanged, repeat inside a batch collapses with the first row winning, empty and 101-row batches fail
- [x] 2.2 Cover the shared-lookup and atomic cases; verify new tests: two rows with the same new industry and the same new location succeed with one row each, a blank name in row 3 or country code `XX` saves nothing (companies, industries and locations counts unchanged), a repeated location in one row is collapsed, another user's same-named company does not match
- [x] 2.3 Regenerate `schema.graphql`, update `docs/STATUS.md` (`createCompanies`, result shape, limits, existing-name rule) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS describes the existing-name rule

## 3. AI parsing of companies

- [x] 3.1 Add `CompanyParsePromptQueries.GetCompanyParsePromptAsync` and the `companyParsePrompt { version instructions }` query with a `CompanyParsePrompt` record (output shape `[{name, industryName, locations:[{city, countryCode}]}]`, `<posting>` data-only wording, ISO country codes, `null` over inventing, the user's industry names max 100); verify a new test asserts the user's industries, `<posting>` and the version appear
- [x] 3.2 Add `ParseMode.Companies`; in `ListingParser` choose the prompt by a switch on the mode, expect an array for `Quick` and `Companies`, and log operation `parse-companies`; verify new tests with `FakeChatClient`: array answer → SUCCEEDED and no company rows created, non-array → FAILED "couldn't be read", the system message equals `companyParsePrompt` instructions, the `LlmUsage` row has the new operation, blank text fails with `InvalidParseRequestError`
- [x] 3.3 Regenerate `schema.graphql`, update `docs/STATUS.md` (`ParseMode.COMPANIES`, `companyParsePrompt`, operation name) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS mentions both paths

## 4. Final check

- [x] 4.1 Run `dotnet test` and `openspec validate add-company-bulk-create`; verify both pass, `schema.graphql` is current, and `.claude/commit-notes.md` has a line for each group
