# Proposal

## Why

`applyToListing` requires a CV, but some applications send no file: form-only portals, and older applications whose
CV is lost. Those can't be tracked today without uploading a fake PDF.

## What Changes

- `applyToListing.cvDocumentId` becomes optional (`Int` instead of `Int!`). Non-breaking for clients: existing calls
  keep working.
- Applying without a CV moves SAVED → APPLIED and marks no CV as sent. The channel ("applied via form") goes in the
  existing `note`; no new field.
- Given document ids keep their current checks (must exist on the listing, must be the right kind) and are marked sent
  and re-dated as before.
- The 2026-09-27 decision "Apply requires a CV" in `docs/STATUS.md` is replaced.

## Capabilities

### New Capabilities

### Modified Capabilities
- `applications`: adds a requirement that applying records the step with or without sent documents.

## Impact

- `Features/Applications/ApplicationMutations.cs` (`ApplyToListingAsync` signature, comment).
- `schema.graphql` (regenerated), `ApplicationTests`.
- `docs/STATUS.md` (apply mutation description, decision log).
- No migration: documents already hang off the listing, and delete/archive/undo rules key on status, not sent documents.
