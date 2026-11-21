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
`language: null` uses the listing's `postingLanguage`, or English when it has none. Norwegian order:
E-post / Telefon / Bosted / Fødselsdato and profile links as labelled lines, then Sammendrag, Arbeidserfaring,
Utdanning, Kurs og sertifiseringer, Prosjekter, Språk, Kompetanser, Referanser. When the listing is not remote and
`address` names none of its cities, `generateCvDocument` appends `| Vil flytte til Oslo` / `| Will relocate to
Oslo` to the location line (base CV downloads have no listing, so never). Each language is one line, with a shared
written/spoken level shown once; dates read `Juni 2015 - Nåværende`. A certification shows
validity, not a period: `Mai 2022, gyldig til Mai 2025` / `May 2022, valid until May 2025`, or `Gyldig til …`
alone. A project's `kind` (`WORK`, `PERSONAL`, `COURSEWORK`, `OPEN_SOURCE`, from the profile project) prints
where an experience shows its employer: `Personlig prosjekt | Oslo, Norge`. Languages use NAV's
written/spoken scale (`BEGINNER`, `GOOD`, `VERY_GOOD`, `NATIVE`). `referencesOnRequest` adds a closing
"Referanser: Oppgis ved forespørsel." line. Entry `location` is display text such as `Lagos, Nigeria`.
The UI fills draft bullets/summary from profile translations in the draft language where they exist; both
AI paths also write the summary in that language and the external prompt supplies translated bullet text.

The draft language is resolved as: the optional `language` argument, else the saved draft's `language`,
else the listing's `postingLanguage`, else English. `tailoredCvDraftFromProfile`, both analysis mutations,
`tailoredCvExternalPrompt`, and `previewTailoredCvDraft` (overrides the pasted `content.language`) accept it,
so the UI can switch language and regenerate before saving. There is still one draft per listing; saving
with the new `content.language` replaces it.

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
adjacent evidence appears under its own name.

A listing outside the field of most of the candidate's experience (for example a café or shop job for a developer)
is handled in all CV prompts, built-in and external, without a field flag. The model judges the mismatch from the
listing and the job titles. Headline and summary use the candidate's own job title in that field when they hold
one ("Butikkmedarbeider | Kundeservice og kasse"), with the main field mentioned briefly at most. The other entries
prefer bullets showing transferable work (customer, client, or user contact, teamwork, training others,
responsibility, routines). Where the text is shortened or summarised, technology names, IT jargon (UI/UX,
backend, development process) and technical metrics are cut, overriding the usual keep-the-facts rule; a bullet
only IT people would understand is skipped. Wording keeps the
source's term for who was served: users or clients don't become customers. An entry with no such bullet keeps
title and dates without bullets, so the timeline stays complete. Skills leave out the technical stack, and skill
grouping labels fit the CV's field.

Norwegian IT CV research: content (19 Oct 2026) and skills
guide (20 Oct 2026). Formatting rationale: [norwegian-cv-formatting.md](norwegian-cv-formatting.md).

## Analyze a draft with the API key

The API can analyze one profile section at a time. For work experience, education and projects, provide the profile entry's `sourceEntryId`; skills and summary use `null`. The result is a suggestion only: the UI applies selected bullet/skill IDs to its editable candidate, then the user reviews it.

```graphql
mutation ($input: AnalyzeTailoredCvSectionInput!) {
  analyzeTailoredCvSection(input: $input) {
    result { status section sourceEntryId selectedBulletIds selectedSkillIds summary headline paragraph technologies bullets { sourceBulletId text } error }
    errors { __typename ... on Error { message } }
  }
}
```

The `SUMMARY` result also carries `headline`: a suggested title line for this CV (≤80 characters asked, in the
draft language). It uses the listing's role title when the job titles support it, otherwise the title they show,
plus the main technology or area; never fullstack or another role the titles don't show. The prompt asks for a
headline every time; `null` still means no suggestion, so keep the draft's headline.

Entry results (experience, education, project) also return `bullets`: the selected bullets' text to apply, in selection order. With the `DETAILED` template it is the full bullet, possibly reworded with the listing's terms; with `CONCISE` it is a bullet the AI wrote from the entry, possibly merging several (`sourceBulletId` = the first merged; see [Templates](#templates)). Either may differ from the profile bullet. Use `bullets[].text` rather than copying the profile bullet.

Sections are `EXPERIENCE`, `EDUCATION`, `PROJECT`, `SKILLS`, and `SUMMARY`. Experience and project results
also carry `technologies`: at most 6 of the entry's own skills (draft-language names, most relevant first) that
are concrete technologies the selected bullets show were used, never practices such as TDD or code review, AI
coding assistants, or a skill picked only because the listing names it; `null` means no suggestion, so keep the entry's current list. `analyzeAllTailoredCv(input:{listingId, model})` runs one request per work experience, then each education entry, then each project, followed by skills and summary; it returns a result for each section. Both analysis mutations accept an optional `model` selection ID from `llmModels`; omitted uses the effective default. Each attempted call is logged against its actual provider and credential. AI analysis does not save the draft.

Configure personal keys through the [provider-key API](llm-provider-keys.md). Credential rejection, rate limiting, timeout, and provider errors return readable failed section results; no automatic key/provider fallback occurs. The UI can prompt users to switch models or update/remove the key. Shared Gemini is used only when a Gemini selection has no personal Gemini key.

## Templates

The analysis mutations, `tailoredCvExternalPrompt` and `previewTailoredCvDraft` take an optional `template` (`DETAILED` when omitted). The draft stores `content.template` only so the UI can restore its toggle; the server never reads the saved value for AI calls, so pass the current toggle. Switching templates does not re-run anything: analyze again to get suggestions for the other template.

- `DETAILED`: 1-2 pages, full bullets (reworded with the listing's terms where they fit, see below), paragraph suggestions, summary ≤280 characters, about 25-30 skills.
- `CONCISE`: a one-page CV a recruiter can scan in seconds. Short overview paragraph suggestions (≤2 sentences, about 250 characters) about the role and what carries over; the server drops one that states a number or listing skill the entry's bullets don't, and the PDF then joins the bullets. `contentStyle` applies as in `DETAILED`. No per-entry bullet caps: each entry is told it shares one page with the others; the shared space rules decide what to keep. Paragraphs are written in the draft language. A bullet showing a listing requirement not yet covered is preferred over a second bullet on the same theme. Summary 2 sentences ≤220 characters, about 15-20 skills.

Both templates may reword a bullet with the listing's term for work the bullet shows (ATS and recruiter matching), e.g. "with product, design, and mobile teams" → "within cross-functional product, design, and mobile teams"; the prompt forbids copying listing sentences or stacking listing phrases. A tailored `DETAILED` CV also tightens each bullet toward the listing (cuts filler, repeated context, and details the listing does not care about, aiming for one or two lines) but never merges bullets. `CONCISE` treats the profile as raw notes: the AI writes each bullet (about 100 characters, at most 140) in the candidate's style, merging related bullets of one entry and condensing long lists to the items that matter for the listing, so a hiring manager can scan each entry's key points. The server checks `DETAILED` bullets word by word (at most 2 words in neither the source bullet nor the listing, numbers from the source) and `CONCISE` bullets by facts (every number and every listing or profile skill name must appear in the entry's bullets). A bullet failing the check, or blank or over 500 characters, falls back to the source text instead of failing the section or the paste. Both templates' prompts avoid stock CV/AI phrasing (no em dashes, no buzzwords such as "spearheaded" or "resultatorientert").

## Use an external AI

`tailoredCvExternalPrompt(listingId:, template:)` returns versioned, copyable instructions and allowlisted listing/profile data. Paste those instructions into an external AI and copy its JSON response back to the app. The prompt asks for full (`DETAILED`) or shortened (`CONCISE`) bullet text worded to the listing, with IDs, and excludes contact details and other sensitive profile fields.

Preview pasted JSON before applying it to the editable UI candidate:

```graphql
mutation ($input: PreviewTailoredCvDraftInput!) {
  previewTailoredCvDraft(input: $input) {
    candidate { schemaVersion content { headline summary experiences { sourceEntryId employer title bullets { sourceBulletId text } } skills { sourceSkillId name level } } }
    errors { __typename ... on Error { message } }
  }
}
```

The preview validates JSON shape and that source IDs belong to the signed-in user's profile. Pass the same `template` used for the prompt: both templates apply their check above and fall back per bullet (the source or its draft-language translation counts as source words); `CONCISE` also checks paragraphs, and `content.template` is set. Entries
linked to the profile take their dates, location and (projects) `kind` from the source entry and follow the profile's
order (newest first, ongoing first); missing `technologies` are filled from it (`[]` is kept). Skills take their
profile level and, when the profile has one, its category; languages not in the profile are dropped and the rest take
the profile's levels. The prompt therefore doesn't ask the AI to copy these. It returns normalized candidate content without writing anything. The UI displays the candidate for review/editing; only the explicit `saveTailoredCvDraft` mutation above persists it. Pasted JSON is never stored.

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

Skills analysis returns about 25-30 skills (15-20 for `CONCISE`): listing-relevant skills, the foundations of the stack shown (HTML, CSS and JavaScript with a frontend framework, SQL with databases, Git), since non-technical readers and keyword filters don't infer them, and each area the profile has evidence in. It sees whether each skill was used in work (`usedInWork`) or projects (`usedInProjects`) and includes an unused skill only when the listing names it. All sections are told to surface supported evidence of themes the listing emphasises (for example AI-powered features), keeping AI tool use apart from building AI features. Bullet selection prefers the kind of work the listing emphasises (frontend, API, platform, senior), and client or stakeholder work, domain learning and requirement clarification when the company kind is `CONSULTANCY` or `BROKER` (analysis and external prompt send `companyResearch.kind`). The summary sees experience (with employer and dates), education and project bullets; in `analyzeAllTailoredCv` it also gets `cvBullets`, the bullets the earlier sections selected, and must agree with them (name only strengths the selected bullets or skills show, and reflect the strongest evidence). The external prompt asks the same of its summary. Every CV prompt lists a few essential rules (profile facts only, honest claims, no title the job titles don't show) before the preferences, gives the space rule (1-2 pages, relevant work first, shorten older and less relevant roles only when space runs short, no bullet counts), and restates the task after the profile. The external prompt (version 23) also asks for `content.headline` (null = keep the current one) and puts the listing, including its `url`, first and asks a browsing-capable AI to read the full posting from exactly that `url` when the stored listing is incomplete. It must never search the web for the posting; if the page can't be opened or shows a different posting, it uses the stored fields only.
