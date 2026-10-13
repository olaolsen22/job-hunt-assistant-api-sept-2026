# Tasks

## 1. Setup

- [x] 1.1 Add QuestPDF to `Directory.Packages.props` and the API project, PdfPig to the test project; verify `dotnet build` succeeds
- [x] 1.2 Add a bundled OFL font (regular + bold) as embedded resources, set the Community licence and register the font at startup; verify a smoke render in a test produces bytes starting with `%PDF-`

## 2. Renderer

- [x] 2.1 Implement `TailoredCvPdfRenderer.Render(TailoredCvContent)` with the section order, empty-section omission, date helper and plain skills/languages/links; verify a PdfPig round-trip test asserts every field and section order for a full draft
- [x] 2.2 Add tests for empty sections, `MM/yyyy – Present` dates, contact before first heading, and "Søknadsbehandler"/"efficient office" extraction; verify they pass (fix font/ligature settings if needed)

## 3. Mutation

- [x] 3.1 Add `generateCvDocument(input:{listingId})` with `ListingNotFoundError` and new `TailoredCvDraftNotFoundError`, storing a Cv `Document` via shared file naming; verify integration tests for success (document listed + downloadable PDF), generating twice, missing draft, and another user's listing
- [x] 3.2 Regenerate `schema.graphql` and verify the new mutation and error type appear

## 4. Docs

- [x] 4.1 Update `docs/STATUS.md` (mutation, renderer, QuestPDF decision) via log-progress; verify the full `dotnet test` suite passes
