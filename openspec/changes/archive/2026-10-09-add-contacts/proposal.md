# Proposal

## Why

Many ads name a contact person ("Spørsmål om stillingen? Kontakt Kari Nordmann, 900 00 000"), and the owner wants to
keep those people per company and see which ones belong to a listing. Today there is nowhere to store them, and the
parse prompts drop them.

## What Changes

- New `Contact` entity: a person at a company (name, title, email, phone, LinkedIn/URL, notes), user-owned.
- Listings link to contacts of their own company (many-to-many), so one recruiter can cover several ads.
- GraphQL: `company.contacts`, `listing.contacts`; mutations to add/update/delete a contact; `ListingInput.contacts`
  upserts contacts by name into the listing's company and links them.
- Full listing parse prompt (and server-side parse) returns `contacts: [{ name, title, email, phone, url }]`, taken
  only from the text, never invented. Quick prompt is unchanged (keeps quick add lean).
- Prompt version bump so the UI knows its cached copy-paste prompt is stale.

## Capabilities

### New Capabilities
- `contacts`: storing contact people per company and linking them to listings.

### Modified Capabilities
- `listing-parsing`: the full listing prompt extracts contact people.

## Impact

- `Features/Contacts/` (new: entity, mutations), `Listing.cs`, `ListingMutations.cs`, `Company.cs`,
  `ListingParsePromptQueries.cs`, `AppDbContext` (DbSet + query filter), new migration `AddContacts`.
- `schema.graphql` regenerated; UI needs to render/edit contacts (separate repo).
- Tests: `ContactTests`, additions to `ListingTests` and `ListingParsePromptTests`.
