# Check listing URLs before AI processing

Collect job URLs first, call `checkListingUrls`, then send only new jobs to the external AI. This check needs no title, company, role, dates, or other AI-generated fields. It uses the API database and does not fetch pages, call an AI, save listings, or log token usage.

## Authenticate and reach the API

The automation's script or tool must be able to reach the API. A remote scheduler cannot reach an API running only on your laptop's localhost. Configure its API base URL and network access separately; an AI prompt alone cannot execute this check.

Use an HTTP client with a cookie jar. POST to `/api/auth/login` with the following body, supplying credentials from the automation's secret storage:

```json
{"email":"<your account email>","password":"<your account password>"}
```

Verify a successful login (`requiresTwoFactor: false` and a non-null `user`), retain the returned session cookie, and send it with subsequent `/graphql` requests. Reuse the session instead of logging in for every batch. Do not commit credentials or cookie files. An unauthenticated GraphQL request returns HTTP 401.

## Check the collected links

POST to `/graphql` with `Content-Type: application/json` and the authenticated session. Put this GraphQL operation in the request's `query` property:

```graphql
query CheckCollectedUrls($urls: [String!]!) {
  checkListingUrls(urls: $urls) {
    index
    url
    normalizedUrl
    existingListingIds
    duplicateOfIndex
    shouldProcess
  }
}
```

The request's `variables` property can contain your NAV URL twice to demonstrate batch repetition:

```json
{
  "urls": [
    "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef",
    "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef?utm_source=automation"
  ]
}
```

If that URL has never been saved by this user, the response is:

```json
{
  "data": {
    "checkListingUrls": [
      {
        "index": 0,
        "url": "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef",
        "normalizedUrl": "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef",
        "existingListingIds": [],
        "duplicateOfIndex": null,
        "shouldProcess": true
      },
      {
        "index": 1,
        "url": "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef?utm_source=automation",
        "normalizedUrl": "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef",
        "existingListingIds": [],
        "duplicateOfIndex": 0,
        "shouldProcess": false
      }
    ]
  }
}
```

If saved listing 42 already uses this normalized URL, both rows instead contain `existingListingIds: [42]` and `shouldProcess: false`. The second row still has `duplicateOfIndex: 0`. Multiple saved attempts return all matching ids in ascending order.

| Field | Meaning |
|---|---|
| `index` | Zero-based position in this request, in the same order as input |
| `url` | Original string, including any surrounding whitespace |
| `normalizedUrl` | URL under the same rules used when saving listings |
| `existingListingIds` | This user's saved matches, including archived and closed listings |
| `duplicateOfIndex` | First earlier input with this normalized URL; null for its first occurrence |
| `shouldProcess` | True only when there is no saved match and no earlier batch occurrence |

Send 1–100 URLs per request. URLs must be absolute HTTP(S) links and at most 2048 characters after trimming. An invalid batch size or URL fails the entire check with top-level GraphQL `errors`, with no partial results. Invalid URL messages identify the zero-based input index. Check both HTTP status and GraphQL `errors` before dispatching any jobs to AI.

## Filter the whole incoming list

The UI can accept the complete master-list JSON, such as this supplied row:

```json
[
  {
    "title": "Production Worker",
    "companyName": "Eterni Norge AS- avd. PLL Bergen",
    "jobRoleName": "Production Worker",
    "url": "https://arbeidsplassen.nav.no/stillinger/stilling/93fec960-d51d-46b1-8eb8-8ccd08608bef",
    "postedOn": "2026-10-10",
    "deadlineType": "DATE",
    "deadlineOn": "2026-12-31",
    "locations": [
      { "city": "Bergen", "countryCode": "NO" }
    ]
  }
]
```

Keep these objects in the UI. Extract their URLs for the API check, then use each result's `index` to select the original object. For one batch of 1–100 rows:

```js
// rows is the parsed master-list JSON. checkUrls uses the generated GraphQL
// client and returns data.checkListingUrls, throwing on HTTP or GraphQL errors.
const results = await checkUrls(rows.map(row => row.url));
const newRows = results
  .filter(result => result.shouldProcess)
  .map(result => rows[result.index]);
```

`newRows` preserves the complete listing objects: title, company, role, dates, locations, and any other supplied fields. The sample returns unchanged when its URL is new, or an empty array when already saved. The API returns check results; the UI constructs the filtered JSON array. Show how many rows remain and how many were skipped, and pass the retained objects to the existing preview/import or further AI processing flow. Check `schema.graphql` for the query contract and generated types.

If collection happens before AI extraction, the same mapping works with rows shaped as `{url}`. If the external AI already produced the complete JSON, this check saves work on subsequent processing only.

For more than 100 rows, keep a set of normalized URLs across chunks for this run. The API indices and batch-repeat detection apply to each individual request. This example accepts `checkUrls`, an authenticated GraphQL client function that returns `data.checkListingUrls` and throws on HTTP or GraphQL errors:

```js
async function filterCollectedJobs(rows, checkUrls) {
  const seen = new Set();
  const eligible = [];
  for (let offset = 0; offset < rows.length; offset += 100) {
    const chunk = rows.slice(offset, offset + 100);
    const results = await checkUrls(chunk.map(row => row.url));
    for (const result of results) {
      if (result.shouldProcess && !seen.has(result.normalizedUrl)) {
        eligible.push(chunk[result.index]);
      }
      seen.add(result.normalizedUrl);
    }
  }
  return eligible;
}

// Call the external AI only after this step succeeds.
const newJobs = await filterCollectedJobs(collectedJobs, checkUrls);
```

## Matching limits

- Normalization trims whitespace, lowercases scheme/host, drops default ports, tracking parameters (`utm_*`, `fbclid`, `gclid`), fragments, and trailing slashes. Meaningful query parameters, their order, and path case remain significant.
- Different URLs can describe the same job. This check does not perform cross-board or company/title matching.
- Archived and closed attempts count as seen. A deliberately reposted or changed ad can be revisited manually; creating a new attempt at a URL used only by closed listings remains allowed.
- Unsaved jobs are not remembered across automation runs. Saving a listing makes it visible to later checks; merely checking a URL does not.
- The check reserves nothing. Another request may save the URL after a check; create-time duplicate enforcement remains authoritative.
- Token savings start when this step runs before the AI reads/processes each job. Filtering JSON the AI already generated cannot recover tokens spent generating it.
