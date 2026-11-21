# Base CVs (UI integration)

A base CV is a reusable CV for a kind of role ("Frontend", "Generic") that is sent as-is. It can exist in several
languages; each language has its own numbered versions, and every save that changes the content is a new version. A
CV document generated from a version records it, so outcomes can later be compared per base CV, per language and
version, and against tailored CVs. The UI may call them "CV templates"; in
the API, `template` means the `DETAILED`/`CONCISE` layout, which is part of each version's content.

Two separate streams:

| Stream | How | Document `origin` |
|---|---|---|
| Base CV | `generateBaseCvDocument(listingId, baseCvVersionId)`, then `applyToListing` | `BASE_CV` + `baseCvVersion` |
| Tailored CV | the listing's tailored draft → `generateCvDocument`, then `applyToListing` | `TAILORED` |

Editing a CV for one listing is the tailored stream; it never links to a base CV. Uploaded PDFs are `UPLOADED`.
Documents created before origins were recorded have `origin: null`.

## Create and version

Content uses the tailored draft's `TailoredCvContentInput` and the same editor, validation and limits.

```graphql
query ($language: Language!) { baseCvFromProfile(language: $language) { summary experiences { sourceEntryId employer title bullets { sourceBulletId text } } } }

mutation ($input: CreateBaseCvInput!) {          # { name, description?, language, content, note? }
  createBaseCv(input: $input) {
    baseCv { id name languages latestVersion { id number language } }
    errors { __typename ... on Error { message } ... on DuplicateBaseCvError { existingBaseCvId } }
  }
}

mutation ($input: SaveBaseCvVersionInput!) {     # { baseCvId, language, content, note? }
  saveBaseCvVersion(input: $input) { baseCvVersion { id number note createdAt } errors { __typename ... on Error { message } } }
}
```

- `baseCvFromProfile` is an unsaved prefill: the whole profile in the requested language, with source ids.
- Names are unique per user, ignoring case. `createBaseCv`'s `language` is the language of version 1; saved content
  always takes the version's language.
- `saveBaseCvVersion` numbers per language: a language the base CV doesn't have yet starts at 1. It returns the
  latest version in that language unchanged (same `id` and `number`, note ignored) when the content didn't change;
  otherwise the next number. Versions never change afterwards.
- `updateBaseCv(input:{id, name, description?})` edits only the name and description.

## Draft with an external AI

Cherry-picks a CV for a kind of role from the master profile: copy the prompt to an external AI, paste its JSON reply
into the preview, review in the editor, then save with `createBaseCv` or `saveBaseCvVersion`. Nothing is saved before that.

```graphql
query ($language: Language!, $targetRole: String!, $notes: String, $template: TailoredCvTemplate, $rewrite: Boolean! = false, $purpose: BaseCvPurpose! = ROLE) {
  baseCvExternalPrompt(language: $language, targetRole: $targetRole, notes: $notes, template: $template, rewrite: $rewrite, purpose: $purpose) { version instructions }
}

mutation ($language: Language!, $json: String!, $template: TailoredCvTemplate, $rewrite: Boolean! = false) {
  previewBaseCv(input: { language: $language, json: $json, template: $template, rewrite: $rewrite }) {
    candidate { schemaVersion content { summary experiences { title bullets { sourceBulletId text } } } }
    errors { __typename ... on Error { message } }
  }
}
```

- `targetRole` (1-100 characters) is the kind of job, such as "Frontend developer". `notes` (at most 2000 characters,
  optional) add context on the target, such as the employers, domains, or strengths to aim at. The AI uses them for
  emphasis, never as facts about the candidate. Invalid values are a GraphQL error on the prompt query.
- `purpose` is `ROLE` by default: the CV is matched to that kind of job like a listing. `OPEN_APPLICATION` is for
  åpen søknad: `targetRole` is the market or field approached (such as "Frontend-leaning developer" or
  "Customer-facing roles"), and there are no requirements to answer. The CV shows the main focus first, then the
  breadth the evidence supports. `template` and `rewrite` work as for `ROLE`; for a market outside IT use
  `rewrite: true`, or IT jobs mostly show only title and dates. The preview takes no `purpose`.
- Prompt version 7, for both purposes: areas the notes ask to show (for example "also show backend") count as
  relevant, and the notes win over the selection preferences, never over the essential rules (profile facts only,
  honest claims, no title the job titles don't show). When the notes ask to show a career direction, the summary may
  end with one factual transition clause backed by profile evidence; other motivation is left to the application
  message. There are no per-role bullet counts: the CV fits 1-2 pages (`CONCISE`: one), keeps bullets that show a
  distinct capability, and shortens older, less relevant roles only when space runs short. Skills: about 25-30
  (`CONCISE` 15-20), always with the stack's foundations. The headline is never null and uses the target's role title
  when the job titles support it. The prompt contains no listing rules.
- `template` is `DETAILED` by default. Pass the same `language`, `template` and `rewrite` to the preview.
- `rewrite: true` lets the AI reword bullets, paragraphs and the summary in plain words, following `notes` (for
  example IT work explained for a shop job). For a target outside the candidate's field, IT entries keep at least one
  bullet translated into everyday words instead of showing only title and dates. The preview then checks facts instead of wording: a bullet or paragraph
  may not name a number or a profile skill (name or translation) that its entry's bullets don't. A failing bullet
  falls back to its source text and a failing paragraph becomes `null`. Meaning isn't checked, so review rewrites.
- `DETAILED` (without `rewrite`) copies relevant bullets exactly and drops the rest, like a tailored draft: the
  preview rejects bullet text that differs from its source or its translation in that language. Every job and school stays.
- `CONCISE` shortens relevant bullets, and it and `rewrite` may combine less relevant ones of one entry into one short
  bullet whose `sourceBulletId` is the first combined bullet. The preview checks each such bullet's words against the
  bullets of its own entry: at most 2 new words and no new numbers; a failing bullet falls back to its source text.
  A paragraph gets the same check with up to 4 new words, so without `rewrite` it stays in the bullets' language; one
  that fails becomes `null`. The check looks at words, not
  meaning, so review combined bullets.
- `InvalidTailoredCvDraftError`: invalid JSON, a bullet without `sourceBulletId`, a source that isn't in that entry
  of this profile, or (`DETAILED`) changed bullet text.
- The result fills name, email, phone, address, links, and date of birth from the profile, plus the headline when the
  AI returned none, so it can be saved as-is.
- Like the tailored preview, entries take their dates, location and project kind from the profile and follow its
  order; skills and languages take the profile's levels, and languages the profile lacks are dropped.
- A combined or shortened bullet differs from its source, so `baseCvTranslation` keeps its text and lists it in
  `untranslatedFields`.

## Add a language

```graphql
query ($baseCvId: Int!, $language: Language!, $fromLanguage: Language) {
  baseCvTranslation(baseCvId: $baseCvId, language: $language, fromLanguage: $fromLanguage) {
    fromLanguage untranslatedFields content { summary experiences { title bullets { sourceBulletId text } } }
  }
}
```

An unsaved prefill: the latest version in `fromLanguage` (default: the newest version in any other language) with
text copied from the profile swapped for the profile's translations through the source ids (titles, degrees,
fields, bullets, places, skill and technology names, and summary/headline when unchanged from the profile).
`untranslatedFields` lists paths such as `summary`, `experiences[0].bullets[2]` or `projects[1].paragraph` that kept
their text: edited after the prefill, no profile source, or no translation yet. Save it with `saveBaseCvVersion` in
the new language. Null when there's no version to translate from. Languages aren't kept in sync afterwards.

## List, archive, delete

```graphql
query ($archived: Boolean!) {
  baseCvs(archived: $archived) { id name description languages archivedAt updatedAt latestVersion { id number language } versions { id number language note createdAt } }
}
query ($id: Int!) { baseCvVersion(id: $id) { number language note content { summary skills { name } } baseCv { name } } }
```

`baseCvs` returns active ones by default (`archived: true` for archived ones), ordered by name. `languages` is in the
order they were added; `versions(language?)` is newest first and `latestVersion(language?)` the newest (null for a
language without versions); without `language` both cover every language. `setBaseCvArchived(input:{id, isArchived})` hides one from the picker but keeps its versions for analysis.
`deleteBaseCv(input:{id})` only works while no document uses any of its versions; otherwise it returns
`BaseCvInUseError`, so archive it instead.

## Preview and send

`GET /api/base-cv-versions/{id}/pdf` renders a version inline for preview (file name `{name}_{EN|NO}_v{n}.pdf`; cookie auth; another user's version is
404). Nothing is stored.

To send one, generate a CV document on the listing and apply with it like any other CV:

```graphql
mutation ($listingId: Int!, $baseCvVersionId: Int!) {
  generateBaseCvDocument(input: { listingId: $listingId, baseCvVersionId: $baseCvVersionId }) {
    document { id fileName origin baseCvVersion { number language baseCv { name } } }
    errors { __typename }
  }
}
```

`Listing.documents { origin baseCvVersion { number language baseCv { name } } }` shows which version an application
sent.
