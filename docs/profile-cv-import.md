# English CV import

The API supports imports into empty or populated English-primary profiles. Parsing and preview do not save profile content. The review UI belongs to the separate Nuxt repository.

## Built-in AI

Paste English plain text or Markdown (1–100,000 characters). Markdown is optional. The complete accepted text is sent to the selected configured provider with the same instructions as the external prompt. Pass an optional `model` selection ID from `llmModels`; omitted uses the effective default. Keys are configured separately through the [provider-key API](llm-provider-keys.md). Raw text and unreviewed results are not saved as parse jobs; only AI usage metadata is stored under `parse-profile-cv`.

```graphql
mutation Parse($text: String!) {
  parseProfileCv(input: { text: $text }) {
    result {
      status error
      candidate {
        schemaVersion contentLanguage
        basics { fullName headline summary email phone }
        experiences { employer title startMonth endMonth skillNames bullets }
        skills { name level }
        warnings { path message }
      }
    }
    errors { __typename ... on InvalidParseRequestError { message } }
  }
}
```

`SUCCEEDED` includes a candidate for review; incomplete values appear as warnings. `FAILED` has no candidate and a readable error (invalid output, invalid/unavailable credential, provider failure, rate limit, or timeout). Blank/oversized input returns typed `InvalidParseRequestError` without a provider call or usage record. Large output that cannot be decoded fails without partial profile changes: retry with a smaller CV section.

## External AI

1. Copy `profileCvParsePrompt { version instructions }`.
2. Supply that prompt and your English CV directly to your chosen external AI. Use any file/text format that service accepts; the API takes no file, CV text, or external key for prompt retrieval.
3. Paste the resulting JSON into `previewProfileCvImport`.
4. Review and correct warnings, then select what to save through `importProfileCv`.

```graphql
mutation Preview($json: String!) {
  previewProfileCvImport(input: { json: $json }) {
    candidate {
      schemaVersion contentLanguage
      basics { fullName headline summary email phone }
      experiences { employer title startMonth endMonth skillNames bullets }
      projects { name url startMonth endMonth skillNames bullets }
      skills { name level }
      warnings { path message }
    }
    errors { __typename ... on InvalidProfileImportError { message } }
  }
}
```

Candidate example:

```json
{
  "schemaVersion": 1,
  "contentLanguage": "ENGLISH",
  "basics": { "fullName": "Example Person", "headline": "Frontend developer" },
  "projects": [{ "name": "Portfolio", "skillNames": ["Vue"], "bullets": ["Built an accessible portfolio."] }],
  "skills": [{ "name": "Vue", "level": null }]
}
```

The candidate supports basics (name, headline, summary, email, phone, location), typed links, hobbies, experience, education, certifications, projects, skill tags, standalone skills, and English/Norwegian language proficiency. Sensitive personal details, references, and job preferences are excluded. No CV tailoring, translation, or PDF/image/Word extraction is performed by the API.

Incomplete candidates are valid previews: warnings identify missing required dates/levels and invalid field values. Dates use `YYYY-MM-01`; year-only dates must be corrected by the user, not guessed by AI. Null end months mean current/ongoing when saved, so confirm them during review. Standalone skill levels need a reviewed 1–5 value; language proficiency needs a reviewed CEFR value. Entry skill tags have the existing profile behavior: a missing profile skill is added at level 1, existing levels stay unchanged.

Malformed shapes/types, unsupported versions/properties, null collection items, duplicate JSON properties, non-English contentLanguage, and JSON over 200,000 characters fail with `InvalidProfileImportError`. Candidate collections allow 100 entries per history section, 20 bullets per entry, 200 skills/tags, 20 languages, 20 hobbies, 10 links, and 1000 supplied warnings.

## Save reviewed selections

Query `profile` before review. For each candidate, choose add, update an existing entry by id, or skip (omit it). Build a typed selection from the corrected values; candidate JSON is not executed directly. Existing data is not automatically matched or deduplicated.

```graphql
mutation Save($selection: ProfileCvImportSelectionInput!) {
  importProfileCv(input: { selection: $selection }) {
    profile {
      fullName headline
      projects { id name bullets { id text position } }
    }
    errors { __typename ... on Error { message } }
  }
}
```

Example variables adding a project:

```json
{
  "selection": {
    "contentLanguage": "ENGLISH",
    "basics": { "headline": "Frontend developer" },
    "projects": [{
      "name": "Portfolio",
      "skillNames": ["Vue"],
      "bullets": [{ "text": "Built an accessible portfolio." }]
    }],
    "skills": [{ "name": "Vue", "level": 3 }]
  }
}
```

- Basics use partial updates: omitted values stay unchanged; explicit null clears an optional value. Whitespace is trimmed; blank strings clear optional text.
- `links` and `hobbies` are replaced only when supplied; send `[]` to clear, not null. Other existing lists and preferences are preserved.
- Selected entry updates require their complete structural fields, including values to retain. For example an experience needs `id`, `employer`, `title`, `startMonth`, and its reviewed optional `endMonth`, `location`, `skillNames`. Omitted optional structural fields clear that entry's corresponding values. Required startMonth on experience/education cannot be null.
- A selected entry without `id` is added. With `id`, it updates that entry and preserves its identity. Omitted entries remain unchanged.
- Bullet selections `{id, text}` update only that existing bullet on its parent; `{text}` appends. Omitted bullets stay unchanged. Existing bullet ids/positions and translations survive; changed sources make translations stale. Import cannot delete or reorder bullets.
- Standalone selected skills/languages upsert reviewed levels (languages by case-insensitive name, with written and spoken levels); omitted ones stay unchanged. The contentLanguage marker refers to the imported prose, not the language proficiency section.
- Import requires an English-primary profile. It never switches an existing Norwegian-primary profile or deletes translations; use the existing language mutation explicitly when permitted. Norwegian translations on an English profile do not block import.
- Invalid selections roll back the whole request including new lookups. Errors include `InvalidProfileImportError` with a selection path, `InvalidLocationError`, and `ProfileEntryNotFoundError` for foreign/unknown/wrong-parent ids.
- Disable double-submit in the UI. Repeating an add can create duplicates; explicit updates preserve ids but do not provide optimistic concurrency control. Refresh profile data before constructing reviewed updates.

The Nuxt follow-up needs built-in text parsing, external prompt copy and JSON paste, a common editable preview with warnings, add/update/skip choices, and a single reviewed save request. No upload picker is required for the built-in flow.
