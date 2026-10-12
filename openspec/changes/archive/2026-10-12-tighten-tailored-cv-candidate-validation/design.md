# Design

## Context

External candidate preview deserializes versioned JSON into the same input shape used by draft saves, then normalizes content and validates source ownership. The JSON contract is strict about unknown properties, but nullable array elements can still reach normalization. Draft saves intentionally allow edited text to retain its source IDs.

## Goals / Non-Goals

**Goals:**
- Make external candidate preview errors deterministic for null list items.
- Preserve trustworthy attribution when an external candidate includes a profile bullet ID.
- Keep the user-editable save contract unchanged.

**Non-Goals:**
- Changing GraphQL signatures, draft persistence, or schema migrations.
- Requiring all candidate text to originate from the profile; generated summaries and user-authored text remain allowed.

## Decisions

### Keep stricter source-text checks on external preview only

During preview, resolve referenced bullet IDs within the signed-in user's profile and compare normalized candidate bullet text with the source text. Reject mismatches as candidate validation errors. Keep the common source-ID ownership checks for both preview and save, but do not impose source-text equality on draft saves because users can edit draft text while retaining attribution.

The alternative is to validate equality for every draft save. That would prevent fabricated attribution but also block the supported editing workflow. Another alternative is to clear the source ID when text changes; that hides useful provenance and forces the client to manage attribution loss.

### Validate null array items before normalization

Check all external candidate section arrays and nested bullet arrays for null entries before mapping them into normalized content. Return the existing typed invalid-draft error with a readable path. Do not catch arbitrary runtime exceptions as validation errors; explicitly reject malformed shape at the input boundary.

The alternative is a broad catch around normalization. That would mask programming errors and turn unrelated failures into client errors.

## Risks / Trade-offs

- [Whitespace or serialization differences could reject otherwise equivalent external text] → Use the same trim normalization applied to candidate text before comparing; do not case-fold or alter internal wording.
- [A new preview-only check could drift from save validation] → Share source ownership resolution where practical and cover both preview rejection and edited-save acceptance in tests.

## Migration Plan

No database migration is required. GraphQL operation names and input/output types remain unchanged. Existing saved drafts are not revalidated or modified.
