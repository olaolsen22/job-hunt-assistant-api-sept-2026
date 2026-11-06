namespace JobHunt.Api.Llm;

// Stored on usage rows and receipts and returned to the UI, so the values must not change.
public static class LlmErrorCodes
{
    public const string InvalidKey = "invalid-key";
    public const string CredentialUnavailable = "credential-unavailable";
    public const string RateLimited = "rate-limited";
    public const string Timeout = "timeout";
    public const string ProviderError = "provider-error";
    public const string InvalidJson = "invalid-json";
    public const string Canceled = "canceled";
    public const string AttemptPending = "attempt-pending";
    public const string ParseFailed = "parse-failed";
    public const string InvalidRequestId = "invalid-request-id";
    public const string RequestConflict = "request-conflict";
    public const string OperationInProgress = "operation-in-progress";
    public const string AiBusy = "ai-busy";
    public const string ResultUnavailable = "result-unavailable";
    public const string OperationInterrupted = "operation-interrupted";
    public const string DailyLimitReached = "daily-limit-reached";
    public const string Forbidden = "forbidden";
    public const string InvalidInput = "invalid-input";
    public const string Conflict = "conflict";

    // A failure every further call in a batch would hit too.
    public static bool StopsBatch(string? code) =>
        code is InvalidKey or CredentialUnavailable or RateLimited or Timeout or ProviderError or DailyLimitReached;

    public static string MessageFor(string code) => code switch
    {
        InvalidKey => "The provider rejected the API key. Update it or switch models.",
        CredentialUnavailable => "The saved API key is unavailable. Re-enter it or switch models.",
        RateLimited => "The AI provider's rate limit was reached. Try later or switch models.",
        Timeout => "The AI took too long to answer. Try again.",
        _ => "The AI provider couldn't be reached. Try again later.",
    };
}
