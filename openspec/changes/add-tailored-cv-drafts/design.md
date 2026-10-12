# Design

## Context

See proposal.md for motivation. The profile stores reusable structured content and stable bullet IDs. Listings are application attempts and own uploaded PDF documents, which are immutable once uploaded. The current plan already requires AI CV prompts to exclude sensitive fields. The existing profile CV import flow provides a precedent for external prompts, candidate JSON, review, and an explicit save mutation.

## Goals / Non-Goals

**Goals:**

- Keep a saved plain-text tailored draft attached to the listing it was prepared for.
- Make API-key analysis useful with Gemini 3.1 Flash through small, bounded section requests.
- Let a whole-profile action run the same section analyses as one user operation, while allowing another model strategy later.
- Make the external-tool path return candidate JSON to the UI for review and explicit save.

**Non-Goals:**

- Rendering a PDF or changing uploaded/sent document behavior.
- Automatically changing the master profile or applying a draft.
- Sending sensitive profile fields to an AI provider or embedding them in an external prompt.

## Decisions

### Section analysis is the API-key primitive

The API-key flow analyzes one requested profile section at a time. An experience request receives the listing context and one experience's metadata, tags, and bullets, then returns relevant bullet IDs and any bounded text suggestion. Skills and summary are separate section requests. “Analyze all” orchestrates the applicable section requests and combines their candidate results; it does not require Gemini to reason over the entire profile in one large prompt. A later model can add a whole-profile strategy behind the same candidate/review contract.

This favors bounded, source-grounded results over a single broad call. The alternative is one call over the full profile, which is simpler to orchestrate but puts more cross-section reasoning pressure on the current model.

### Analysis results are transient until save

Section and whole-profile analysis return candidate data only. External JSON follows the same contract: the server validates its shape and ownership references, then returns candidate data for the client to populate and edit. A separate save operation persists the reviewed content. This mirrors the existing CV import boundary and prevents unreviewed model output from changing profile or application state.

### Draft belongs to the listing and snapshots its text

Persist the editable plain-text sections on a user-owned draft associated with one listing. Save/update edits the current draft for that listing. Preserve source bullet IDs as attribution where available, alongside the submitted text; never derive saved text dynamically from the profile. This keeps the application-specific version stable when the master profile changes. Drafts remain distinct from `Document`, which represents an uploaded PDF and the exact file sent with an application.

The initial scope uses one editable working draft per listing. Version history can be added later if users need to compare unsent drafts; the sent document remains the record of what was submitted.

### Use the same allowlist for both AI routes

Build analysis inputs from an explicit allowlist of listing and profile fields. Include CV content needed to select or compose sections, but exclude date of birth, nationality, work authorization, contact details, references, and job-search preferences. External prompts must use the same allowlist. Do not persist raw provider prompts or external-tool input as analysis history; retain existing LLM usage metadata for provider calls.

This follows the project plan's existing privacy boundary. Contact details can be entered or edited in the reviewed draft without being sent to either AI route.

### Keep section content structured as plain text

Represent candidate and saved drafts as named text sections (for example, summary, experience entries and bullets, skills, education, certifications, and projects), with optional source IDs for selected profile items. This supports UI editing and external JSON validation without introducing document layout, templates, or file-generation concerns. The client can render or copy these fields as plain text.

## Risks / Trade-offs

- [More provider calls in analyze-all mode] → Keep each request narrow, log each provider call through existing LLM usage tracking, and allow a user to request only a section.
- [Model may select irrelevant or unsupported content] → Return source IDs, keep generated text distinguishable, and require user review before save.
- [External JSON may be malformed or contain untrusted text] → Validate the candidate shape and source ownership, display it as editable text, and persist only after explicit save.
- [Profile changes make a saved draft out of date] → Preserve the saved text snapshot; later profile edits do not rewrite it.

## Migration Plan

Add the draft persistence schema with a migration. Existing listings and documents require no data conversion. Rollback removes only unsent draft data; uploaded and sent documents remain untouched.
