## 1. Mutation

- [x] 1.1 Add `ParseUrlAsync(string url, …)` to `ParseMutations` with `[Error<InvalidParseRequestException>]`: trim,
  validate absolute http/https and max length 2048, fetch via `ListingPageFetcher`, run Full mode
  through `RunJobAsync`
- [x] 1.2 Regenerate `schema.graphql`

## 2. Tests

- [x] 2.1 `ParseTests`: success with a JobPosting page returns a Full job whose raw text is the extracted text
- [x] 2.2 Invalid URLs (empty, relative, `ftp:`, too long) return the error without fetching or creating a job
- [x] 2.3 Unreadable page (403 / no text) returns the error and creates no job
- [x] 2.4 `dotnet test` passes

## 3. Docs

- [x] 3.1 Update `docs/STATUS.md` with the new mutation; add a commit-notes line
