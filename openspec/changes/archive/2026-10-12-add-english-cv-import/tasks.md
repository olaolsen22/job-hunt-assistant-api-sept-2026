# Tasks

## 1. Candidate format and external prompt

- [x] 1.1 Add versioned English candidate DTOs and a common bounded JSON decoder under Features/Profiles, including nullable incomplete values and field-path warnings; verify focused tests for valid complete/incomplete candidates, malformed JSON, wrong shapes/types, unsupported versions/properties, 200k limit, collection limits, year-only dates, and unknown levels.
- [x] 1.2 Add profileCvParsePrompt with no CV arguments, extraction rules, exact JSON format, and version; verify GraphQL tests that the prompt preserves English wording, never invents dates/levels, excludes unsupported sections, includes no existing profile data, and makes no AI call or usage record.
- [x] 1.3 Add previewProfileCvImport using the decoder and InvalidProfileImportError; verify external JSON produces the shared structured candidate/warnings, malformed input has typed errors, and profile/lookup/usage counts remain unchanged.
- [x] 1.4 Start docs/profile-cv-import.md with the external copy-prompt/supply-CV/paste-JSON flow, schema example, incomplete-data warnings, and supported/excluded fields; verify the example passes the common decoder and regenerate schema.graphql through SchemaTests.

## 2. Built-in English text parsing

- [x] 2.1 Add a profile-specific keyed IChatClient parser and parseProfileCv transient result with full accepted text, shared prompt, JSON mode, input/output limits, and DI registration; verify FakeChatClient tests for plain text, Markdown, blank/over-limit input without calls, no 12k truncation, prompt equality, and valid/incomplete results with no profile or lookup writes.
- [x] 2.2 Add parse-profile-cv usage logging and readable invalid-output/provider/rate-limit/timeout failures using TimeProvider and existing token accounting; verify tests for each failure, token counts, distinct operation, and no retained CV text in ParseJob or logged errors.
- [x] 2.3 Extend docs/profile-cv-import.md with the built-in operation, limits, statuses/errors, and shared review flow; verify documented GraphQL operations against SchemaTests and regenerate schema.graphql.

## 3. Selective atomic import

- [x] 3.1 Extract only necessary internal validation/apply helpers from existing profile mutations without changing public full-replace semantics; verify the existing Features.Profiles tests still pass before adding the import mutation.
- [x] 3.2 Add typed reviewed import selections and importProfileCv for optional basics/location/link/hobby changes, create/update entries, selected skill/language upserts, and explicit bullet updates/appends; verify tests on empty and populated profiles for omission vs null, all entry kinds, preserved unrelated data, explicit levels, lookup reuse, unchanged preferences/target locations, and tag-to-profile-skill sync.
- [x] 3.3 Preserve entry/bullet ids and translations, reject duplicate target ids and wrong-parent bullets, and leave omitted bullets unchanged; verify tests for appending order, bullet limits across existing plus imported data, translated bullet/source-summary edits becoming stale, and unchanged translations on untouched fields.
- [x] 3.4 Validate all targets through user filters and save in one transaction with typed field-specific errors and English-primary checks; verify owner/guest isolation, unknown/wrong-kind ids, Norwegian-primary rejection without language changes, English profiles with Norwegian translations succeeding, incomplete selected values rejected, and rollback of profile plus lookup writes when a later selection fails.
- [x] 3.5 Extend docs/profile-cv-import.md with complete selected-save examples, add/update/skip guidance, list replacement semantics, no automatic deduplication/idempotency, and the separate Nuxt UI follow-up; verify examples through integration tests and regenerate schema.graphql.
- [x] 3.6 Update relevant docs/STATUS.md through log-progress and docs/PLAN.md to reflect English import and its deferred bilingual/PDF/generation scope; verify documentation matches the shipped operations and append the rationale to .claude/commit-notes.md.

## 4. Integration verification

- [x] 4.1 Run the full dotnet test suite once after runtime work and verify existing manual profile CRUD, listing parsing, translation behavior, and SchemaTests remain green without starting the API or touching the real database.
- [x] 4.2 Run openspec validate add-english-cv-import --strict and review the final diff for scope, unexpected migrations, and API documentation consistency; verify validation passes and only intended feature files/artifacts changed.
