# Proposal

## Why

Companies only come into existence as a side effect of `createListing`, so you can't add one on its own, and a company
carries only a name and one head-office location. You want to pre-create companies (one or a pasted list), say what
business each is in, give them several locations, and optionally have an AI read the list for you, the way quick add
already works for listings.

## What Changes

- Add `createCompanies(input: {companies: [{name, industryName?, locations: [{city?, countryCode}]}]})`: validates every row, collapses repeats by normalized name, saves in one transaction, returns `[{company, created}]`. A name that already exists is returned with `created: false`, not an error. One company is a list of one.
- Add `Industry`: a per-user lookup (`Name`, `NormalizedName`, unique `(UserId, NormalizedName)`), found or created by name. `Company.IndustryId?` points at one industry.
- Add `CompanyLocation` join table so a company has several locations. **BREAKING:** `Company.LocationId` is removed, with no separate head office; existing values are copied into the join table.
- **BREAKING:** `updateCompany(input:{id, name, location})` becomes `updateCompany(input:{id, name, industryName?, locations})`.
- `Company` GraphQL type gains `industry` and `locations`; add an `industries(search)` query for autocomplete.
- Add AI parsing of companies: `ParseMode.Companies` on `parseListing` (the AI only proposes; nothing is saved) and a `companyParsePrompt` query for the copy-paste route to an external AI. Both use the same prompt text. The UI previews the rows, then calls `createCompanies`.

## Capabilities

### New Capabilities
- `companies`: creating, updating and listing companies, with their industry and locations, including bulk create and duplicate handling.
- `company-parsing`: turning pasted text (a list of names, or a company page) into proposed company rows, server-side or through the copy-paste prompt.

### Modified Capabilities

## Impact

- `Features/Companies` (`Company`, `CompanyMutations`, `CompanyQueries`), new `Features/Industries`, `Data/AppDbContext` (query filters, `DbSet`s), one migration (new tables, copy `LocationId` into `CompanyLocation`, drop the column).
- `Features/Parsing` (`ParseMode`, `ListingParser` prompt choice and JSON shape check) and a new company prompt query next to `ListingParsePromptQueries`.
- `Features/Locations/LocationLookup`: needs a per-batch cache, since it returns a fresh unsaved `Location` each call and two rows sharing a new location would otherwise both insert it.
- `schema.graphql`, `docs/STATUS.md` (including a BREAKING note for the UI), and tests.
- No new packages.
