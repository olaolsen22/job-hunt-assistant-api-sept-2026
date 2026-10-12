# Design

## Context

See proposal.md for motivation. Features/Profiles already stores relational history, stable bullet ids, translations, references, and preferences. Existing updateProfile and entry mutations are full replacements; calling them sequentially during import would clear omitted values and permit partial saves. ListingParser demonstrates keyed Gemini IChatClient, shared external prompts, inline execution, TimeProvider, and LlmUsage. Its 12k truncation and persistent ParseJob.RawText are unsuitable for CV import. The API and Nuxt UI are separate repositories.

The durable profile-history spec still mentions removed descriptions/highlights and says tagging does not add a profile skill. Later profile-bullets and profile-entry-details specs and current code supersede those statements: import uses bullets and one-way level-1 tag sync. This change does not revive old fields or silently rewrite unrelated specs.

## Goals / Non-Goals

Goals: one reusable extraction contract; review before writes; an atomic, selective import that can be repeated on a populated profile; existing identities survive updates.

Non-goals: persisted import jobs/drafts, automatic entity matching or deduplication across imports, destructive replacement, and changes to existing listing parse contracts. UI implementation belongs to a separate change in the Nuxt repository.

## Decisions

### 1. Separate profile operations, reuse the AI infrastructure

Add profileCvParsePrompt, parseProfileCv, previewProfileCvImport, and importProfileCv under Features/Profiles. A profile-specific parser injects the existing keyed Gemini IChatClient and options. parseProfileCv returns a transient result `{status: SUCCEEDED|FAILED, candidate, error}` and logs operation `parse-profile-cv`; it does not extend ParseMode or persist ParseJob. Follow existing timeout, provider-error and token accounting behavior. Alternatives: adding PROFILE_CV to parseListing obscures the domain boundary and persists raw CV text; a new queue/job table is unnecessary for inline parsing.

### 2. One versioned JSON contract with incomplete candidates

Use schemaVersion 1 and contentLanguage ENGLISH. Candidate fields: basics (fullName, headline, summary, email, phone, location), links, hobbies, experiences, educations, certifications, projects, skills, languages, and warnings. Entries carry their current profile fields plus candidate bullets as text; they have no database ids. Month dates use YYYY-MM-01 where known. Unknown dates and levels remain null; warnings identify paths such as experiences[0].startMonth. A missing end date is not asserted to mean current when the CV is ambiguous: flag the ambiguity for review. Source prose stays English; never invent a month for year-only dates. Skill tags require evidence in that entry. Standalone skill levels and CEFR levels require explicit evidence or user completion.

Decode into candidate DTOs with nullable required-for-save fields, strict property/type/version checks, and bounded collection sizes; preview distinguishes decodable-but-incomplete candidates from malformed data. Unknown/unsupported fields are rejected so an external result cannot silently import unsupported data. JSON text limits: 200k; per candidate section maximum 100 entries, each up to 20 bullets; skills maximum 200; languages limited to supported enum values. Normal field length issues become warnings in preview and errors if selected for save. Prompt describes the exact contract and limits. The external prompt has no input arguments and includes no existing profile data; the user supplies a document directly to their external AI.

### 3. Built-in input is pasted text, not a Markdown requirement

Accept complete plain text or Markdown up to 100k characters; reject rather than truncate over-limit input. Delimit it as untrusted CV data and instruct extraction without following instructions in the document. Use JSON response mode, temperature 0, and a bounded output budget; invalid/truncated output fails through the common decoder. No provider capability research or PDF extraction is required for this text-only scope. Alternative: mandating Markdown adds work without improving the API boundary.

### 4. Reviewed selections are a separate typed write contract

Candidate JSON is never executed as an import. importProfileCv accepts a typed selection DTO. Optional<T> fields distinguish omission from explicit null for basics/location and optional list selections. Include only chosen entries; id absent means create, id present means update. Entry structural fields use the existing complete entry input shape for selected updates, with reviewed values including any existing values to retain. Bullets are independent selections `{id?, text}`: update matching child ids, append new bullets, leave all omitted bullets untouched. No delete or reorder action. Reject repeated target ids, invalid kinds, and bullet ids on the wrong parent. User chooses the target entry/bullet using the existing profile query; no fuzzy matching sends the existing profile to AI.

Skills and languages are explicit reviewed upserts; existing levels change only when those selections are present. Entry skill tags keep existing add-missing-profile-skill-at-level-1 behavior. References, sensitive details, preferences, and targetLocations remain untouched. Repeating an add request can create duplicates; no idempotency guarantee or automatic matching is added. Documentation must tell the UI to disable duplicate submission and offer add/update/skip decisions during review. Explicit updates preserve ids and are safe to repeat with the same values.

### 5. Atomic validation and saving

Use shared internal profile validation/apply helpers without invoking public mutations that each SaveChanges. Extract only the helpers actually needed from ProfileMutations, ProfileEntryMutations, ProfileBulletMutations, and related skill/language handling. Load all selected targets through current user filters, validate ownership/parent relationships and combined resulting limits, then apply within one transaction and save. Lookups created during preparation are included in that transaction and rolled back on failure. Return ProfileQueries.LoadAsync after success. Keep the existing error types for validation/not-found/location and add InvalidProfileImportException for format/language/selection errors with a field path.

Alternative: sequential existing GraphQL mutations reuse validation but cannot guarantee atomicity and have unsafe omission semantics. A generic patch framework or repository layer would expand the change without a need.

### 6. English-only import preserves current language rules

The fixed English contract means no language picker is needed for this version. Saving checks that the primary profile language is English; it never silently switches an existing Norwegian profile. Users may explicitly change the language through the current mutation when allowed. Existing Norwegian translations on an English profile survive source edits and become stale as currently specified. Importing translated CVs and changing language constraints need their own design later.

### 7. Privacy boundary and provenance

Only the user-supplied CV reaches built-in AI; existing profile content is never added. A pasted CV can itself contain personal details or references; the extraction schema excludes sensitive fields/references but does not claim to redact raw input. Do not persist raw input or candidate content in ParseJob, LlmUsage errors, or logs. Only reviewed content is saved. External AI provenance is not assumed to be reliable, and imported originals do not use the translation isMachine flag.

## Risks / Trade-offs

- Incomplete CV dates or proficiency → nullable candidates and explicit review corrections before selected records save.
- Duplicate imports → explicit existing-id updates and UI add/update/skip choices; no automatic matching in this version.
- Large candidates exceeding model output budget → fail clearly without writes; users can import sections separately.
- Stale review overwrites an explicitly selected field changed elsewhere → no new concurrency model; clients refresh the profile before constructing updates. Unselected data remains unchanged.
- Norwegian primary profiles cannot save English imports → return a clear limitation without changing existing language or translation state.
- Shared helper extraction could change manual CRUD → retain its full-replace semantics and run all existing profile tests plus the full suite.

## Migration Plan

No schema or data migration is expected. Ship additive GraphQL operations and regenerate schema.graphql; document both UI flows and errors. Rollback removes the new API operations; already imported data remains ordinary profile data. Do not run the API or alter the real database during implementation verification. A separate UI change consumes the committed schema and builds review/save interaction.
