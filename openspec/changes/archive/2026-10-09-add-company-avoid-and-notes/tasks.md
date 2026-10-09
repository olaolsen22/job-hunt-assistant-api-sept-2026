# Tasks

## 1. Schema

- [x] 1.1 Add `Company.AvoidedAt` (`DateTime?`, comment: null = not avoided, independent of favorite) and `Company.Notes` (`string?`, Markdown), then generate `make migration name=AddCompanyAvoidAndNotes`; verify `dotnet build` passes and the migration only adds two nullable columns to `Companies`

## 2. Avoid flag

- [x] 2.1 Add `CompanyMutations.SetCompanyAvoidedAsync(id, isAvoided)` with `[Error<CompanyNotFoundException>]` (`??=` on avoid, `null` to clear); verify new `CompanyTests`: avoid sets a time, un-avoid clears it, avoiding twice keeps the first time, unknown id and another user's company → `CompanyNotFoundError`
- [x] 2.2 Cover filtering and independence; verify new tests: `companies(where: {avoidedAt: {neq: null}})` returns only avoided companies, avoiding a favorite leaves `favoritedAt` unchanged (and the reverse), `updateCompany` and `createCompanies` keep `avoidedAt`, and an avoided company's listing is still returned with non-null `company.avoidedAt`

## 3. Notes

- [x] 3.1 Add `CompanyMutations.SetCompanyNotesAsync(id, notes)` with `[Error<CompanyNotFoundException>]` and `[Error<InvalidCompanyException>]`, using `ApplicationMutations.CleanNote` and `MaxNoteLength`, with the length checked before saving; verify new tests: notes are trimmed and replaced, blank and null clear to null, 10,001 chars → `InvalidCompanyError` with stored notes unchanged, unknown or foreign id → `CompanyNotFoundError`
- [x] 3.2 Cover independence; verify new tests: `updateCompany` and `createCompanies` keep notes, and `setCompanyNotes` leaves `avoidedAt` and `favoritedAt` unchanged

## 4. Docs and final check

- [x] 4.1 Regenerate `schema.graphql`, update `docs/STATUS.md` with `/log-progress` (`Company.AvoidedAt`/`Notes`, both mutations, migration name), and append the why to `.claude/commit-notes.md`; verify `SchemaTests` passes and STATUS lists both mutations
- [x] 4.2 Run `dotnet test` and `openspec validate add-company-avoid-and-notes`; verify both pass
