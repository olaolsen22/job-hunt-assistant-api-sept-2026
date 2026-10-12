# Tailored CV drafts

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
      location
      links { label url }
      summary
      experiences {
        sourceEntryId
        employer
        title
        startMonth
        endMonth
        bullets { sourceBulletId text }
      }
      educations { sourceEntryId school degree field startMonth endMonth bullets { sourceBulletId text } }
      certifications { sourceEntryId name issuer issuedMonth expiresMonth url bullets { sourceBulletId text } }
      projects { sourceEntryId name url startMonth endMonth bullets { sourceBulletId text } }
      skills { sourceSkillId name level }
      languages { language level }
    }
  }
}
```

The query returns `null` when the listing has no draft or is not owned by the signed-in user.

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

Sections are `EXPERIENCE`, `SKILLS`, and `SUMMARY`. `analyzeAllTailoredCv(input:{listingId})` runs one request per work experience, followed by skills and summary; it returns a result for each section. Each provider call is logged separately. AI analysis does not save the draft.

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

```graphql
mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
  saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
    tailoredCvDraft { id createdAt updatedAt content { summary } }
    errors { __typename ... on Error { message } }
  }
}
```

Send the complete edited candidate in `content`, including contact fields the user entered; omitted sections have no content. A foreign or missing listing returns `ListingNotFoundError`. The API enforces the signed-in user's ownership, and draft saves do not change listing status or sent documents.
