## Context

`CreateListingAsync` filters candidates with `l.CompanyId == entity.CompanyId`, then keeps those with the same
`JobRoleId` or normalized title. By that code a different company can't match, and the single-call test for it
passes. Quick add sends N `createListing` calls aliased in one request; Hot Chocolate runs mutations serially but on
one scoped `AppDbContext`, so the batch is the only situation the tests don't cover. The cause is not yet found.

## Decisions

- **Repro first.** Add a batch test (two rows, same title/role, different new companies, then two rows with an
  existing company each) before touching the logic. Where it fails decides the fix; don't guess.
- **Suspects to check, in order:**
  1. `CompanyId` is 0/unset at the `sameCompany` query when `entity.Company` is a new, tracked entity and the
     context already tracks others (the filter would then match on a wrong id).
  2. The company lookup in `ApplyAsync` resolving two different names to one company (`Names.Normalize` is
     trim + upper only).
  3. The UI showing matches from the same batch instead of the payload (only if 1 and 2 are clean; then it's a UI
     report, not an API change).
- **Fix in place** in `CreateListingAsync`; no new abstraction. Compare on the `Company` navigation or the saved id,
  whichever the repro shows is reliable.
- **No loosening of the same-company rule** unless asked: it is the owner's decision from 2026-09-27.

## Risks / Open Questions

- If the repro passes (no API bug), the report likely comes from the same company appearing under two names, or from
  the UI. Then this change shrinks to the added regression test plus a note, and the owner is asked for one real pair.
- Within one company, matching on role is broad (e.g. "Developer"). Left unchanged; raise only if the owner reports it.
