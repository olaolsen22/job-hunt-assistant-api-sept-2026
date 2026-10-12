# Proposal

## Why

The profile is a reusable source of CV content, while each listing represents a distinct application attempt. Users need a way to select and tailor profile content for a specific listing, review it as plain text, and save it before deciding how to produce or submit a document.

## What Changes

- Add a listing-linked plain-text CV draft based on selected profile content.
- Support AI analysis either for an individual profile section on demand or for the whole profile in one operation.
- Support the configured API-key AI flow and an external-tool flow using a copyable prompt and pasted JSON.
- Return suggestions for review and editing; save only when the user explicitly submits the draft.
- Keep PDF generation and document rendering out of scope.

## Capabilities

### New Capabilities

- `tailored-cv-drafts`: Analyze profile content for a listing, review and save a plain-text tailored CV draft linked to that listing.

### Modified Capabilities

None.

## Impact

- API profile and listing GraphQL features, with draft persistence associated with a listing/application attempt.
- Gemini-backed analysis and existing LLM usage logging for API-key requests.
- Existing external CV import prompt/JSON review pattern as a precedent for the copy-and-paste flow.
- The listing detail UI in the separate Nuxt repository will consume analysis results, present edits, and call the save endpoint; UI implementation is outside this API change.
- No PDF generation or changes to sent-document behavior.
