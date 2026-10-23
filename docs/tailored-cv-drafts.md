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
      sectionOrder
      links { label url }
      summary
      experiences {
        sourceEntryId
        employer
        title
        location
        startMonth
        endMonth
        technologies
        bullets { sourceBulletId text }
      }
      educations { sourceEntryId school degree field location startMonth endMonth bullets { sourceBulletId text } }
      certifications { sourceEntryId name issuer issuedMonth expiresMonth url bullets { sourceBulletId text } }
      projects { sourceEntryId name kind url location startMonth endMonth technologies bullets { sourceBulletId text } }
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
Prosjekter, Språk, Kompetanser, Lenker, Referanser; dates read `Juni 2015 - Nåværende`. A certification shows
validity, not a period: `Mai 2022, gyldig til Mai 2025` / `May 2022, valid until May 2025`, or `Gyldig til …`
alone. A project's `kind` (`WORK`, `PERSONAL`, `COURSEWORK`, `OPEN_SOURCE`, from the profile project) prints
where an experience shows its employer: `Personlig prosjekt | Oslo, Norge`. Languages use NAV's
written/spoken scale (`BEGINNER`, `GOOD`, `VERY_GOOD`, `NATIVE`). `referencesOnRequest` adds a closing
"Referanser: Oppgis ved forespørsel." line. Entry `location` is display text such as `Lagos, Nigeria`.
The UI fills draft bullets/summary from profile translations in the draft language where they exist; both
AI paths also write the summary in that language and the external prompt supplies translated bullet text.

`sectionOrder` picks the PDF's section order; saving `null` keeps `STANDARD`. The UI offers it as a per-draft
toggle (e.g. "Developer layout"); neither AI path sets it.

| Value | Order after the contact lines |
|---|---|
| `STANDARD` | Summary, experience, education, courses/certifications, projects, languages, skills, links, references |
| `DEVELOPER` | Links as labelled contact lines (`GitHub: https://…`, no Links heading), summary, skills, experience, projects, education, courses/certifications, languages, references |

`DEVELOPER` also uses the IT headings `Teknisk kompetanse` / `Technical skills` and `Utvalgte prosjekter` /
`Selected projects`, and prints each experience's and project's `technologies` as a muted, non-bold line
after its content (`Teknologier: C#, ASP.NET Core`). `STANDARD` ignores `technologies`. The profile prefill fills
`technologies` with every skill tagged on the entry (draft language, higher profile level first); analysis or
the user trims it to what the listing needs. On save, `null` stays `null`.

AI suggestions keep the candidate's own wording: summaries start from the profile headline/summary and
paragraphs match the voice of the selected bullets, with preference for bullets that show contribution and
results. Both AI paths write paragraphs and the summary in the draft language from translated bullets, add no
metrics, keep claims as strong as their source (team vs own part, no "helped" → "led"), claim no seniority or
management the job titles don't show, and never imply a missing listing skill through a similar tool: the real
adjacent evidence appears under its own name. Norwegian IT CV research: content (19 Oct 2026) and skills
guide (20 Oct 2026). Formatting rationale: [norwegian-cv-formatting.md](norwegian-cv-formatting.md).

## Analyze a draft with the API key

The API can analyze one profile section at a time. For work experience, education and projects, provide the profile entry's `sourceEntryId`; skills and summary use `null`. The result is a suggestion only: the UI applies selected bullet/skill IDs to its editable candidate, then the user reviews it.

```graphql
mutation ($input: AnalyzeTailoredCvSectionInput!) {
  analyzeTailoredCvSection(input: $input) {
    result { status section sourceEntryId selectedBulletIds selectedSkillIds summary paragraph technologies error }
    errors { __typename ... on Error { message } }
  }
}
```

Sections are `EXPERIENCE`, `EDUCATION`, `PROJECT`, `SKILLS`, and `SUMMARY`. Experience and project results
also carry `technologies`: at most 6 of the entry's own skills (draft-language names, most relevant first) that
are concrete technologies the selected bullets show were used, never practices such as TDD or code review, AI
coding assistants, or a skill picked only because the listing names it; `null` means no suggestion, so keep the entry's current list. `analyzeAllTailoredCv(input:{listingId, model})` runs one request per work experience, then each education entry, then each project, followed by skills and summary; it returns a result for each section. Both analysis mutations accept an optional `model` selection ID from `llmModels`; omitted uses the effective default. Each attempted call is logged against its actual provider and credential. AI analysis does not save the draft.

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

The preview validates JSON shape and that source IDs belong to the signed-in user's profile. A project's
missing `kind` and an entry's missing `technologies` are filled from its source entry (`[]` is kept). It returns normalized candidate content without writing anything. The UI displays the candidate for review/editing; only the explicit `saveTailoredCvDraft` mutation above persists it. Pasted JSON is never stored.

## Save or replace the draft

`saveTailoredCvDraft` creates the listing's draft the first time and updates that same draft on later saves. Source IDs are optional attribution; the saved text is a snapshot and does not change when the profile is edited.

The draft sets one `contentStyle` (`BULLETS` default or `PARAGRAPH`) for every experience, education, certification and project, so the CV prints consistently. Each entry keeps its own optional `paragraph`. Paragraph mode renders the editable `paragraph` text without a bullet. Per-experience and per-education AI analysis returns selected source bullet IDs and, when it can form one from them, a concise paragraph suggestion. The PDF falls back to joining the selected bullet texts if no paragraph is supplied. Review generated wording before saving.

```graphql
mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
  saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
    tailoredCvDraft { id createdAt updatedAt content { summary } }
    errors { __typename ... on Error { message } }
  }
}
```

Send the complete edited candidate in `content`, including contact fields the user entered; omitted sections have no content. A foreign or missing listing returns `ListingNotFoundError`. The API enforces the signed-in user's ownership, and draft saves do not change listing status or sent documents.

## Selection guidance

Skills analysis returns listing-relevant skills plus the candidate's core professional stack (usually about 12-20), not only listing keywords; it sees whether each skill was used in work (`usedInWork`) or projects (`usedInProjects`), keeps foundational skills such as Git, SQL, testing and CI/CD when the listing wants them, and includes an unused skill only when the listing asks for it. All sections are told to surface supported evidence of themes the listing emphasises (for example AI-powered features), keeping AI tool use apart from building AI features. Bullet selection prefers the kind of work the listing emphasises (API, platform, senior). The summary sees experience (with employer and dates), education and project bullets. The external prompt (version 11) puts the listing, including its `url`, first and asks a browsing-capable AI to read the full posting when the stored listing is incomplete.
