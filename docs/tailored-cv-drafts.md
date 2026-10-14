# Tailored CV drafts

Built-in analysis accepts optional `requestId` and shares the [AI call safeguards](llm-call-safeguards.md). Generate one UUID per analysis action and reuse it for transport retries. Analysis previews remain transient, so completed retries return `result-unavailable` rather than rerunning or storing suggestions.

`analyzeAllTailoredCv` returns section `status` values `SUCCEEDED`, `FAILED`, or `SKIPPED`, plus a nullable safe `errorCode`. For example, successful skills analysis followed by credential rejection returns the skills suggestions, a failed attempted section with `invalid-key`, and skipped later sections with that code. A pre-dispatch `daily-limit-reached` makes that section and all later sections skipped. Skipped sections have no provider call or usage row. Provider/credential failures stop the batch; a section's `invalid-json` permits later independent sections. Cancellation propagates without further calls. Review completed suggestions and save explicitly as before.

A tailored CV draft belongs to one listing, so each saved copy stays with the application attempt it was prepared for. The draft is editable plain text and is separate from PDF `Document` records. Saving it does not apply to the listing or mark a document as sent.

## Read the current draft

```graphql
query ($listingId: Int!) {
  tailoredCvDraft(listingId: $listingId) {
    id
    createdAt
    updatedAt
    content {
      fullName
      headline
      email
      phone
      address
      dateOfBirth
      language
      referencesOnRequest
      links { label url }
      summary
      experiences {
        sourceEntryId
        employer
        title
        location
        startMonth
        endMonth
        bullets { sourceBulletId text }
      }
      educations { sourceEntryId school degree field location startMonth endMonth bullets { sourceBulletId text } }
      certifications { sourceEntryId name issuer issuedMonth expiresMonth url bullets { sourceBulletId text } }
      projects { sourceEntryId name url location startMonth endMonth bullets { sourceBulletId text } }
      skills { sourceSkillId name level }
      languages { name written spoken }
    }
  }
}
```

The query returns `null` when the listing has no draft or is not owned by the signed-in user.

`language` (`ENGLISH`/`NORWEGIAN`) decides the PDF's headings, contact labels, and date words. Saving with
`language: null` uses the listing's `postingLanguage`, or English when it has none. Norwegian follows NAV's CV:
E-post / Telefon / Adresse / Fødselsdato, then Sammendrag, Arbeidsforhold, Utdanning, Kurs og sertifiseringer,
Prosjekter, Språk, Kompetanser, Lenker, Referanser; dates read `Juni 2015 - Nåværende`. Languages use NAV's
written/spoken scale (`BEGINNER`, `GOOD`, `VERY_GOOD`, `NATIVE`). `referencesOnRequest` adds a closing
"Referanser: Oppgis ved forespørsel." line. Entry `location` is display text such as `Lagos, Nigeria`.
The UI fills draft bullets/summary from profile translations in the draft language where they exist; both
AI paths also write the summary in that language and the external prompt supplies translated bullet text.
Formatting rationale: [norwegian-cv-formatting.md](norwegian-cv-formatting.md).

## Analyze a draft with the API key

The API can analyze one profile section at a time. For work experience, provide the profile entry's `sourceEntryId`; skills and summary use `null`. The result is a suggestion only: the UI applies selected bullet/skill IDs to its editable candidate, then the user reviews it.

```graphql
mutation ($input: AnalyzeTailoredCvSectionInput!) {
  analyzeTailoredCvSection(input: $input) {
    result { status section sourceEntryId selectedBulletIds selectedSkillIds summary error }
    errors { __typename ... on Error { message } }
  }
}
```

Sections are `EXPERIENCE`, `EDUCATION`, `SKILLS`, and `SUMMARY`. `analyzeAllTailoredCv(input:{listingId, model})` runs one request per work experience, then each education entry, followed by skills and summary; it returns a result for each section. Both analysis mutations accept an optional `model` selection ID from `llmModels`; omitted uses the effective default. Each attempted call is logged against its actual provider and credential. AI analysis does not save the draft.

Configure personal keys through the [provider-key API](llm-provider-keys.md). Credential rejection, rate limiting, timeout, and provider errors return readable failed section results; no automatic key/provider fallback occurs. The UI can prompt users to switch models or update/remove the key. Shared Gemini is used only when a Gemini selection has no personal Gemini key.

## Use an external AI

`tailoredCvExternalPrompt(listingId:)` returns versioned, copyable instructions and allowlisted listing/profile data. Paste those instructions into an external AI and copy its JSON response back to the app. The prompt asks for exact source bullet text and IDs, and excludes contact details and other sensitive profile fields.

Preview pasted JSON before applying it to the editable UI candidate:

```graphql
mutation ($input: PreviewTailoredCvDraftInput!) {
  previewTailoredCvDraft(input: $input) {
    candidate { schemaVersion content { summary experiences { sourceEntryId employer title bullets { sourceBulletId text } } skills { sourceSkillId name level } } }
    errors { __typename ... on Error { message } }
  }
}
```

The preview validates JSON shape and that source IDs belong to the signed-in user's profile. It returns normalized candidate content without writing anything. The UI displays the candidate for review/editing; only the explicit `saveTailoredCvDraft` mutation above persists it. Pasted JSON is never stored.

## Save or replace the draft

`saveTailoredCvDraft` creates the listing's draft the first time and updates that same draft on later saves. Source IDs are optional attribution; the saved text is a snapshot and does not change when the profile is edited.

Each experience, education, certification and project can set `contentStyle` to `BULLETS` (the default) or `PARAGRAPH`. Paragraph mode renders the editable `paragraph` text without a bullet. Per-experience and per-education AI analysis returns selected source bullet IDs and, when it can form one from them, a concise paragraph suggestion. The PDF falls back to joining the selected bullet texts if no paragraph is supplied. Review generated wording before saving.

```graphql
mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
  saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
    tailoredCvDraft { id createdAt updatedAt content { summary } }
    errors { __typename ... on Error { message } }
  }
}
```

Send the complete edited candidate in `content`, including contact fields the user entered; omitted sections have no content. A foreign or missing listing returns `ListingNotFoundError`. The API enforces the signed-in user's ownership, and draft saves do not change listing status or sent documents.
