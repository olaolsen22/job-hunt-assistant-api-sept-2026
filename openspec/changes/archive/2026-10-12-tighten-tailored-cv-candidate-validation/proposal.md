# Proposal

## Why

The external CV preview currently accepts a valid source bullet ID alongside different text, which can make an unverified rewrite look like source-backed profile content. Some malformed JSON shapes containing null list items can also escape normal validation and produce an internal error instead of a useful client error.

## What Changes

- Require externally attributed bullet text to match the referenced profile bullet exactly; keep normal saved-draft text editable.
- Reject null items in pasted candidate arrays with a readable validation error and no persistence.
- Add regression coverage for mismatched source text and null items at supported nesting levels.

## Capabilities

### New Capabilities

- `tailored-cv-drafts`: Defines safe validation behavior for external candidate previews while preserving explicit review and editable draft saves.

### Modified Capabilities

None.

## Impact

Changes are limited to the tailored CV external preview and candidate validation contract, related tests, and the OpenSpec capability specification. No GraphQL operation signatures or persistence schema changes are expected.
