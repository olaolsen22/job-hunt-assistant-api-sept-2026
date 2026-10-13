# Proposal

## Why

Tailored CV drafts hold reviewed, listing-specific CV content, but the only way to attach a CV to an application is to upload a PDF made elsewhere. Rendering the draft directly closes that gap, and the output must survive ATS text extraction because that is how most employers read it.

## What Changes

- New GraphQL mutation `generateCvDocument(input:{listingId})` renders the listing's saved tailored CV draft to a PDF and stores it as a `Document` (Kind = Cv) on that listing, the same way an upload is stored. It returns the new `Document`.
- Standard single-column CV with a real, extractable text layer. The section order is: contact, headline/summary, experience, education, certifications, projects, skills, languages, links.
- ATS-safe content: embedded Unicode font (æøå, ligatures extract as plain text), contact details in the page body, consistent `MM/yyyy` dates, plain-text skills (no level graphics), English section headings.
- Each call creates a new unsent document; existing documents are untouched (delete old ones with `deleteDocument`).
- Out of scope: visual layout/templates, localized headings, DOCX output, a REST download change (the existing download endpoint serves the result).

## Capabilities

### New Capabilities
- `cv-pdf-generation`: Rendering a tailored CV draft into an ATS-readable PDF document attached to a listing.

### Modified Capabilities

## Impact

- Code: new `Features/TailoredCvDrafts` renderer + mutation; reuses `Document`/`DocumentContent` storage and file naming from `Features/Documents`.
- Dependencies: QuestPDF (API project), PdfPig (test project only), one bundled open-licence font.
- GraphQL schema: one new mutation and error types; `schema.graphql` regenerated. No migration.
