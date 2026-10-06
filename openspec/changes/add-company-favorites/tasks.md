# Tasks

## 1. Company favorites

- [x] 1.1 Add `Company.FavoritedAt` (`DateTime?`, comment on why a time and not a bool) and generate `make migration name=AddCompanyFavorites`; verify `dotnet build` passes and the migration only adds a nullable column
- [x] 1.2 Add `CompanyMutations.SetCompanyFavoriteAsync(id, isFavorite)` with `[Error<CompanyNotFoundException>]` (`??=` on favorite, `null` to clear); verify new tests in `CompanyTests`: favorite sets a time, unfavorite clears it, favoriting twice keeps the first time, unknown id → `CompanyNotFoundError`, another user's company → `CompanyNotFoundError`
- [x] 1.3 Cover the shortlist and independence rules; verify new tests: favorites ordered by `favoritedAt` DESC exclude non-favorites, `updateCompany` on a favorite keeps `favoritedAt`, re-sending a company via `createCompanies` keeps it
- [x] 1.4 Regenerate `schema.graphql`, update `docs/STATUS.md` (`Company.FavoritedAt`, `setCompanyFavorite`, migration name) with `/log-progress`, append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS lists the mutation

## 2. Final check

- [x] 2.1 Run `dotnet test` and `openspec validate add-company-favorites`; verify both pass and `schema.graphql` is current
