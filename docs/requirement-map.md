# Requirement map (UI integration)

A requirement map states what one listing needs, which profile bullets and entries are evidence for each need, and
which needs are gaps. It is the "analyze the job" step: read it to decide whether to send a template as-is, tailor a
CV, or skip the listing. There is one saved map per listing. CV and cover letter generation do not read it yet.

The flow is the usual one: get a candidate from the built-in AI or an external chat, review and correct it, then save.
Nothing is stored before `saveRequirementMap`.

## Read

```graphql
query ($listingId: Int!) {
  requirementMap(listingId: $listingId) {
    id createdAt updatedAt
    content { needs { text kind importance match bulletIds entries { kind id } note } }
  }
}
```

`null` when the listing has no saved map or isn't yours. Needs come back in saved order.

| Field | Values |
|---|---|
| `kind` | `SKILL`, `DUTY`, `QUALIFICATION`, `LANGUAGE`, `PRACTICAL` (location, work mode, employment) |
| `importance` | `REQUIRED`, `PREFERRED` |
| `match` | `EVIDENCED`, `PARTIAL` (adjacent or weaker evidence; the note says what is missing), `GAP` |
| `bulletIds` | profile bullet ids, strongest first |
| `entries` | `{ kind: EXPERIENCE \| EDUCATION \| CERTIFICATION \| PROJECT, id }` for a need no single bullet states |

Evidence ids are a snapshot. A bullet or entry deleted later stays in the saved map, so skip ids the profile no longer
has. Compare `updatedAt` with the listing's to hint that a map may be out of date.

## Generate with the API key

```graphql
mutation ($listingId: Int!, $model: String, $requestId: UUID) {
  generateRequirementMap(input: { listingId: $listingId, model: $model, requestId: $requestId }) {
    result { status error errorCode content { needs { text kind importance match bulletIds entries { kind id } note } } }
    errors { __typename ... on Error { message } }
  }
}
```

`result.status` is `SUCCEEDED` with `content` (an unsaved candidate) or `FAILED` with `error` and `errorCode`. It
shares the [AI call safeguards](llm-call-safeguards.md): one UUID per action, reused for transport retries. A listing
with no description and no skills returns `InvalidRequirementMapError` without calling a provider.

## Use an external AI

```graphql
query ($listingId: Int!) { requirementMapExternalPrompt(listingId: $listingId) { version instructions } }

mutation ($listingId: Int!, $json: String!) {
  previewRequirementMap(input: { listingId: $listingId, json: $json }) {
    candidate { needs { text kind importance match bulletIds entries { kind id } note } }
    errors { __typename ... on Error { message } }
  }
}
```

Paste the JSON object from the chat's reply (without the code fence). `InvalidRequirementMapError`: empty or malformed
JSON, an unknown enum value, a missing or wrong `schemaVersion`, or no needs. Pasted JSON is never stored.

Both AI paths correct the candidate instead of rejecting it: evidence ids that aren't in the profile are dropped, a
need left `EVIDENCED` or `PARTIAL` with no evidence and no note comes back as `GAP`, long text is cut, and needs past
30 are dropped. Show the candidate for review; it may differ from what the model wrote.

## Save and delete

```graphql
mutation ($listingId: Int!, $content: RequirementMapContentInput!) {
  saveRequirementMap(input: { listingId: $listingId, content: $content }) {
    requirementMap { id updatedAt content { needs { text match } } }
    errors { __typename ... on Error { message } }
  }
}

mutation ($listingId: Int!) { deleteRequirementMap(input: { listingId: $listingId }) { listingId errors { __typename } } }
```

Saving is a full replace and is strict, unlike the AI paths. `InvalidRequirementMapError` when:

- there are no needs or more than 30;
- a `text` is blank or over 200 characters, or a `note` is over 500;
- a need has more than 10 `bulletIds` or 10 `entries`, or one isn't in your profile;
- a need is `EVIDENCED` or `PARTIAL` with no evidence and no note. Language and practical needs have nothing to point
  at, so give those a note.

A `GAP` is always stored without evidence, whatever was sent. `ListingNotFoundError` for a listing that isn't yours;
`RequirementMapNotFoundError` when deleting a map that doesn't exist. Deleting a listing deletes its map.
