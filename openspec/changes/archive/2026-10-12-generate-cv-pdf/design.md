# Design

## Context

`TailoredCvDraft.Content` already holds typed, user-reviewed CV content per listing. `Document` + `DocumentContent` store PDF bytes per listing, and `applyToListing` sends them. See proposal.md - Why.

## Goals / Non-Goals

**Goals:** correct, complete, extractable content; one plain layout; testable ATS behavior.

**Non-Goals:** templates or visual polish (next proposal), localized headings, DOCX, rendering from the live profile (only the saved draft snapshot is rendered).

## Decisions

- **QuestPDF over HTML→PDF (Playwright) or PdfSharp/MigraDoc.** Pure C# fluent layout in one package, embeds and subsets fonts with a Unicode map, no browser process to run on the VPS. PdfSharp is lower level for the same result. QuestPDF Community licence is set once at startup (`QuestPDF.Settings.License = LicenseType.Community`).
- **Bundled font.** Linux containers often lack fonts with æøå; ship one OFL sans font (e.g. Lato or Source Sans 3, regular + bold) as embedded resources registered via `FontManager.RegisterFont` at startup. No fallback to system fonts, so output is identical everywhere.
- **Pure renderer.** `TailoredCvPdfRenderer.Render(TailoredCvContent) -> byte[]` is a static function with no DB/DI, so tests can render drafts directly. The mutation `GenerateCvDocumentAsync` in the TailoredCvDrafts slice loads the draft (global user filter handles isolation), renders, and stores a `Document` using the shared `DocumentMutations.FileName`. `OriginalFileName` = the generated name.
- **Always a new document.** Replacing an unsent CV would need a "generated" flag or guessing which upload to drop; a new row plus existing `deleteDocument` is simpler and never loses an uploaded file.
- **Errors.** Reuse `ListingNotFoundException`; add `TailoredCvDraftNotFoundException(listingId)`. The 5 MB limit is not applied to generated output (text-only CVs are far smaller).
- **Layout rules (ATS).** Single `Column`, no tables, no page header/footer content except an optional page number; contact block is the first body content; dates formatted by one helper (`MM/yyyy`, `Present`); skills joined with ", "; languages as "Norwegian (C2)"; links as "Label: url" in text.
- **Verification with PdfPig** in tests: extract page text and assert substrings and order. This acts as an ATS proxy and catches font/ligature regressions.

## Risks / Trade-offs

- [HarfBuzz ligature shaping may map "fi" to a glyph without proper ToUnicode] → round-trip test; if it fails, disable the font's ligature feature or choose a font without standard ligatures.
- [QuestPDF licence change] → Community licence covers this personal project; noted in README.
- [Native SkiaSharp dependency on deploy] → QuestPDF ships native assets for linux-x64; verify in the M8 deploy image.
