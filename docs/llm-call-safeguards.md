# AI call safeguards

All six built-in AI mutations accept optional `requestId: UUID`: `parseListing`, `parseUrl`, `parseListingFromUrl`, `parseProfileCv`, `analyzeTailoredCvSection`, and `analyzeAllTailoredCv`. Calls still run inline. No background queue or automatic retry is added.

## Duplicate protection

Generate one UUID per user action. Reuse it, with unchanged arguments, when retrying transport after a lost response. Do not generate a fresh UUID automatically on a network error: a new ID after completion can buy another inference attempt. Older callers can omit the field; they receive active-operation protection but sequential repeats after completion are fresh operations.

```graphql
mutation Parse($text: String!, $request: UUID!) {
  parseListing(input: {text: $text, mode: FULL, requestId: $request}) {
    parseJob { id status result error }
    errors { ... on AiSafetyError { code message } }
  }
}
```

Each user can have one active top-level AI operation across tabs, mutation kinds, and credentials. An identical overlapping request returns `operation-in-progress`; different overlapping work returns `ai-busy`. Rejected work is not queued, makes no inference call, and creates no inference usage row. An analyze-all batch owns one admission for its entire run; its sections share that operation.

Request IDs are scoped to the signed-in user. Reusing an ID with changed arguments returns `request-conflict`. Completed listing operations return their original owned parse job without fetching or calling again, including after default-model or credential changes. Failed listing jobs replay as the same failed job. If the referenced job is no longer available, the API returns `result-unavailable`.

CV candidates and tailored-analysis suggestions remain transient. A completed repeat returns `result-unavailable`; it does not persist or regenerate that preview. The UI should preserve a received preview locally during review. If the response was lost, explain that a new explicit attempt can incur another call before assigning a new ID.

Stable `AiSafetyError.code` values:

| Code | Meaning |
|---|---|
| `invalid-request-id` | Empty UUID; supply a nonempty UUID. Malformed UUID syntax is rejected by GraphQL coercion. |
| `request-conflict` | ID already belongs to different submitted input. |
| `operation-in-progress` | Matching operation is running. |
| `ai-busy` | Other AI work is running, or local admission is contended. |
| `result-unavailable` | Operation finished, but no replayable preview/job is available. |
| `operation-interrupted` | The attempt's outcome is uncertain; review before explicitly starting another. |
| `daily-limit-reached` | User-configured personal-provider allowance is exhausted. |
| `invalid-input` / `forbidden` | Invalid limit/provider or non-Owner settings mutation. |

No admission error includes submitted text, provider bodies, keys, or other users' operation information. Existing input/model validation and no credential/provider fallback remain in force.

## Optional personal-provider limits

Owners can set their own daily request limit per personal-key provider: Gemini, OpenAI, and Mistral. Settings start disabled. Null disables enforcement; zero pauses personal inference; a positive integer sets the maximum daily attempts. The setting can exist before adding a key and survives removing/replacing it. Guests cannot manage personal limits.

```graphql
mutation SetLimit($provider: String!, $limit: Int) {
  setLlmCallLimit(input: {provider: $provider, dailyRequestLimit: $limit}) {
    limit { provider dailyRequestLimit requestsConsumed remainingRequests resetsAt }
    errors { ... on AiSafetyError { code message } }
  }
}

query Limits {
  llmCallLimits {
    provider dailyRequestLimit requestsConsumed remainingRequests resetsAt
  }
}
```

Limits apply to the user's personal-provider attempts across all its models and credential generations, with a midnight Pacific reset matching existing reporting. Enabling or lowering a limit does not clear consumption already recorded today. Remaining allowance is floored at zero, or null when disabled. Non-Owners receive an empty settings list. Queries never call providers.

Shared Gemini inference, including the default free model, is exempt from these daily limits. If the default model instead resolves to your personal Gemini key, your personal Gemini setting applies. Duplicate/concurrency guards and failure stopping protect both paths. `llmUsageToday.requestsLimit` remains an estimated shared-model provider quota; `llmCallLimits.dailyRequestLimit` is the enforced application limit. Neither measures other apps' use of the same key or promises a dollar budget.

Each admitted inference attempt counts once, including failed, canceled, or interrupted attempts. Each analyze-all section is an attempt. Duplicates, locally rejected input/models/credentials, key model-list validation, and skipped sections do not consume allowance. Admission reserves usage before sending; a crash immediately before dispatch can conservatively count an attempt whose delivery is unknown. Pending reservations appear as unsuccessful usage with `attempt-pending`, later updated to the result or a safe interruption code. Report counts as admitted application attempts, not confirmed billable requests.

## Batch stopping

Analyze-all returns ordered `results` with `status: SUCCEEDED | FAILED | SKIPPED` and nullable `errorCode`. Credential rejection/unavailability, provider rate limiting, timeout, or provider failure stops further dispatch. The attempted section fails; later sections are skipped with its safe code. Exhausting a personal daily limit skips the unadmitted section and everything after it. Earlier suggestions remain in the response. Invalid JSON only fails that section; later sections can continue. Caller cancellation propagates and never automatically restarts the batch.

## Durable state and recovery

`AiOperation` stores user/request identity, a protected request digest, safe attribution/state/error/timestamps, and an optional existing parse-job reference. It retains no raw CV input, candidate, tailored suggestion, prompt, secret, or provider body. Receipt states are Running, Succeeded, Failed, and Interrupted. Identities do not silently expire. `UserLlmLimit` stores only the user/provider setting and update time; usage rows hold durable per-operation attempt attribution.

The current deployment supports one API instance. On startup, leftover Running operations become Interrupted and release active admission; pending attempt audits are marked interrupted. Reusing an interrupted ID cannot restart inference. A failed final database save also interrupts the operation; a possibly submitted attempt remains counted. If the database itself is unavailable, admission fails closed until recovery. The system cannot promise provider-side exactly-once execution.

`AddAiCallSafeguards` adds state tables, same-owner relationships, nullable usage attribution, and indexes. Historical BYOK jobs, credentials and token counts are preserved; no limit is enabled by migration. Migration generation uses `make migration name=AddAiCallSafeguards`, which backs up database/WAL/SHM before scaffolding. The migration is applied at the next coordinated API startup, not by generating it.

Protected digests use the existing Data Protection key ring. Back up the database and matching private key ring together; `make backup` alone does not include the ring. Restoring an earlier database loses newer request identities and can make old IDs repeatable, so reconcile interrupted UI actions rather than silently retrying them. Keep the ring and application name stable, as described in [provider-key deployment guidance](llm-provider-keys.md).
