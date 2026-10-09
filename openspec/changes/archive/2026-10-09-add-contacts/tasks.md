## 1. Data model

- [x] 1.1 Add `Features/Contacts/Contact.cs` (Contact, ListingContact, configurations, unique `(CompanyId, NormalizedName)`)
- [x] 1.2 Add `Company.Contacts` and `Listing.Contacts` navigations; DbSets + global query filter in `AppDbContext`
- [x] 1.3 `make migration name=AddContacts` and review the generated migration

## 2. GraphQL

- [x] 2.1 `ContactMutations`: add/update/delete with typed errors and validation
- [x] 2.2 `ListingInput.Contacts` (`ContactInput`), resolve by normalized name within the company, fill-empty merge, replace links on create/update
- [x] 2.3 Expose `company.contacts` and `listing.contacts` (projection-friendly)

## 3. Prompts

- [x] 3.1 Add `contacts` shape and rules to the full listing prompt; bump version; leave quick prompt alone
- [x] 3.2 Make sure the server-side parse result passes `contacts` through

## 4. Tests & docs

- [x] 4.1 `ContactTests`: add, duplicate, invalid, update, delete unlinks, user isolation (company-delete cascade is FK-only: there is no deleteCompany mutation)
- [x] 4.2 Listing tests: new contact created, existing reused + merged, empty list unlinks
- [x] 4.3 Prompt tests: full mentions contacts, quick doesn't
- [x] 4.4 Regenerate `schema.graphql`, run `dotnet test`, append commit notes, `/log-progress`
