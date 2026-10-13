# UI integration: AI keys and safeguards

Regenerate GraphQL types from the updated schema.

## Provider keys and models

- Add Owner-only provider-key settings.
- Keys are write-only: display returned safe metadata.
- Never store or log API keys.
- Send `llmModels.models.id` as the `model` input.
- Provider failures must not automatically switch keys or models.

## Optional daily limits

Use `llmCallLimits` and `setLlmCallLimit`.

- `null`: limit disabled.
- `0`: personal-provider calls paused.
- Positive integer: daily call allowance.
- Display `requestsConsumed`, `remainingRequests`, and `resetsAt`.
- Limits span models and key replacements for that provider.
- Shared Gemini is exempt; personal Gemini follows its configured limit.

## Duplicate protection

All six AI mutations accept optional `requestId`.

- Generate one UUID per user action.
- Reuse it with unchanged inputs for transport retries.
- Never automatically retry with a fresh UUID.
- Disable submission while an operation is running.

Handle `AiSafetyError.code`, especially:

- `operation-in-progress`
- `ai-busy`
- `request-conflict`
- `daily-limit-reached`
- `operation-interrupted`
- `result-unavailable`

## Analysis results

- Support `SUCCEEDED`, `FAILED`, and `SKIPPED`.
- Keep completed suggestions visible.
- Show `errorCode` and the safe error message for failed/skipped sections.
- Save reviewed content only through the existing explicit save flow.

## Lost responses

- Listing retries return the original parse job.
- CV and analysis previews are transient.
- For `result-unavailable` or `operation-interrupted`, require explicit user confirmation before starting another potentially paid attempt.

## Backend references

- [GraphQL schema](../schema.graphql)
- [AI call safeguards](llm-call-safeguards.md)
- [Provider keys and model routing](llm-provider-keys.md)
- [Tailored CV drafts](tailored-cv-drafts.md)
