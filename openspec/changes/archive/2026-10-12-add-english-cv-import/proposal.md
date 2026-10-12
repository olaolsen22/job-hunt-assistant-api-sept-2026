# Proposal

## Why

The master profile supports manual editing but cannot import an existing CV. English CV import will populate or extend it at any time, providing reviewed source content for later tailored CV generation.

## What Changes

- Add built-in parsing of pasted English plain text or Markdown using the configured Gemini key. Markdown is optional; there is no file upload or extraction in this version.
- Add a versioned copyable external-AI prompt with no CV input. Users supply their CV directly to their chosen external service in any format that service accepts and bring back JSON.
- Use one structured candidate format for both paths, including incomplete values and warnings rather than invented dates, proficiency levels, or achievements.
- Add read-only preview of pasted JSON and an explicit, transactional import of reviewed selections. Imports support empty and populated profiles; selected fields can be set, entries added, and identified existing entries updated without clearing unrelated data.
- Preserve existing entry and bullet identities on updates, and use existing translation staleness rules when original text changes.
- Limit imported prose to English and require an English primary profile for saving; never automatically change contentLanguage or delete translations. Parsing and external prompt retrieval remain available regardless of the profile language.

## Capabilities

### New Capabilities

- `profile-cv-import`: English CV extraction through built-in or external AI, candidate preview, and selective saving into the user's master profile.

### Modified Capabilities

None. Existing profile CRUD, language, and translation requirements remain in effect.

## Impact

Add GraphQL operations and import types under Features/Profiles, a profile-specific parser using keyed IChatClient, LlmUsage operation logging, schema export, integration tests, and API integration documentation. Reuse existing profile entities and lookup rules; no new persistent import tables or migration are expected. Existing listing parsing remains unchanged. The separate Nuxt repository will need the review UI in a follow-up; this change supplies its API contract.

Out of scope: API-side PDF/image/Word uploads, Norwegian import, auto-translation, importing a second-language CV as translations, CV tailoring/generation, external provider key management, bulk deletion or whole-profile replacement, and inferred job preferences. Sensitive personal details and references are excluded from the structured import in this version.
