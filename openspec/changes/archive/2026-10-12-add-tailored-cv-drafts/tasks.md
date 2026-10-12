# Tasks

## 1. Draft persistence

- [x] 1.1 Add a user-owned tailored CV draft associated with one listing, store editable plain-text sections and source bullet IDs, and add the migration; verify schema tests cover ownership, listing relationship, and one draft per listing.
- [x] 1.2 Add authenticated query and save/update operations for the current listing draft; verify mutation/query tests cover create, update, foreign listing access, and that save does not change application status or documents.
- [x] 1.3 Document the draft fields and operations in the API integration guide and STATUS; verify the documented shape matches the GraphQL schema.

## 2. API-key analysis

- [x] 2.1 Add allowlisted listing/profile context construction and section analysis for experience bullets, skills, and summary; verify prompt tests confirm sensitive fields are excluded and suggestions reference valid source IDs.
- [x] 2.2 Add analyze-all orchestration over the section analyzer and log each provider call through existing LLM usage tracking; verify tests cover section-only and whole-profile results and provider failure behavior.
- [x] 2.3 Add user-owned listing validation and input/output bounds for analysis requests; verify tests cover foreign listings, missing sections, and oversized or invalid inputs.

## 3. External prompt and candidate review

- [x] 3.1 Add a listing-specific copyable external prompt and JSON contract using the same allowlist as API-key analysis; verify prompt tests exclude sensitive fields and document the output shape.
- [x] 3.2 Add validation of pasted external JSON into candidate draft data, including source item ownership checks; verify tests cover valid candidates, malformed JSON, invalid shape, and foreign source IDs without persistence.
- [x] 3.3 Document the external prompt, paste/review, and explicit save flow; verify the guide shows that candidate parsing does not save until the draft mutation is called.

## 4. Integration

- [x] 4.1 Verify the complete GraphQL schema exposes section analysis, analyze-all, external candidate parsing, and draft save/read operations with authentication; run the focused tailored CV tests and schema validation.
- [x] 4.2 Verify the change's acceptance scenarios are covered and run the full test suite once after implementation; record any provider-dependent manual verification separately.
