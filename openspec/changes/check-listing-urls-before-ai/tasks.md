# Tasks

## 1. Share existing URL normalization

- [x] 1.1 Extract `ListingMutations.NormalizeUrl` and its tracking-parameter predicate into a shared internal helper in `Features/Listings`, and use it in create/update without changing equivalence rules; verify the diff preserves normalization behavior.
- [x] 1.2 Cover normalization regressions through listing create/update tests, including scheme/host casing, default ports, fragments, tracking parameters, trailing slashes, meaningful parameters, and path case; verify with targeted listing tests.

## 2. Add the batch check and caller documentation

- [x] 2.1 Add `checkListingUrls` with the ordered result fields in the delta spec, full-batch validation, one user-filtered no-tracking lookup of matching ids/URLs, and first-occurrence batch detection; verify through integration tests for new URLs, saved URLs, normalized repeats, combined saved/batch matches, and deterministic indices/id order.
- [x] 2.2 Add integration coverage for archived/open matches, all closed statuses and multiple same-URL attempts, owner/guest isolation, authentication, 1/100-row success, 0/101-row failure, indexed invalid URL errors, whitespace, and meaningful URL differences; verify the targeted preflight test suite passes and existing reapplication behavior remains intact.
- [x] 2.3 Verify read-only behavior in integration tests: repeated unsaved checks remain eligible, saving changes later recommendations, listings/parse jobs/usage remain unchanged by checks, and fake page/AI clients are not invoked; run these tests with the existing in-memory `ApiFactory`.
- [x] 2.4 Refresh and review `schema.graphql` through `SchemaTests`, rerunning the schema test after its expected regeneration failure; verify only the intended additive query/result contract appears.
- [x] 2.5 Write `docs/listing-url-preflight.md` with the NAV URL example, query/variables/result example, existing cookie login, caller filtering before AI work, mapping indices to JSON rows, a normalized seen-set across chunks, and API-access prerequisites; verify every field and limit matches the schema/spec and explain unsaved-history, closed-attempt, exact-URL, and concurrency limits. Use `log-progress` to record the implemented behavior in the relevant STATUS section.

## 3. Verify integration

- [x] 3.1 Run the full `dotnet test` suite once after implementation and schema review; verify all tests pass, review the final diff for scope, and confirm no migrations or real database changes were introduced.
