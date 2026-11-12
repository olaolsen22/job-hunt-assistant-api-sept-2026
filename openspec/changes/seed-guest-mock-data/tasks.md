# Tasks

## 1. Create GuestDataSeeder class

- [x] 1.1 Create `Features/Auth/GuestDataSeeder.cs` with a `SeedAsync(IServiceProvider)` method that resolves `AppDbContext`, `UserManager<AppUser>`, and `TimeProvider`
- [x] 1.2 Look up the guest user by configured `Seed:GuestEmail`; return early if the guest has any listings
- [x] 1.3 Seed shared reference data: 2–3 `Location` records (Oslo, Bergen, Trondheim), 3–4 `JobRole` records (e.g. Frontend Developer, Backend Developer, Fullstack Developer, DevOps Engineer) reusing existing categories, and 5–6 `Skill` records (TypeScript, React, C#, .NET, Docker, PostgreSQL) reusing existing categories
- [x] 1.4 Seed 8 `Company` records with normalized names, varied industries, and some with notes/favorites

## 2. Seed listings and application timeline

- [x] 2.1 Seed 10 `Listing` records across the companies, with varied `ListingKind`, `Seniority`, `EmploymentType`, `Schedule`, `WorkMode`, and `Status` values; set `NormalizedTitle` via `Names.Normalize()`
- [x] 2.2 Seed `StatusChange` records for each listing to build a realistic timeline (e.g. Applied listing has Saved → Applied changes; Interviewing has Saved → Applied → Interviewing)
- [x] 2.3 Favorite 1–2 listings by setting `FavoritedAt`; archive 1 rejected listing by setting `ArchivedAt`

## 3. Seed CV profile

- [x] 3.1 Seed a `Profile` with personal details (fictional name, headline, summary, email, phone, hobbies, location)
- [x] 3.2 Seed 3 `ProfileExperience` entries with start/end dates and 2–3 `ProfileBullet` records each
- [x] 3.3 Seed 2 `ProfileEducation` entries (e.g. BSc Computer Science, MSc Software Engineering)
- [x] 3.4 Seed 5–6 `ProfileSkill` records linking the profile to the skills created in 1.3

## 4. Seed base CV template

- [x] 4.1 Seed one `BaseCv` ("General Tech CV") with normalized name
- [x] 4.2 Seed one `BaseCvVersion` with `ContentJson` containing a valid `TailoredCvContent` structure populated from the profile data

## 5. Seed contacts

- [x] 5.1 Seed 3–4 `Contact` records at different companies with names, titles, and some with email/phone
- [x] 5.2 Seed 1–2 `ContactInteraction` records per contact and call `Refresh()` to set `LastInteractionAt`

## 6. Wire up and test

- [x] 6.1 Call `GuestDataSeeder.SeedAsync` from `Program.cs` after `AuthSeeder.SeedAsync`
- [x] 6.2 Write tests: verify seeding populates the expected entities for a guest user, verify idempotency (second call is a no-op), verify owner data is untouched
- [x] 6.3 Run `dotnet test` to confirm all existing tests still pass
