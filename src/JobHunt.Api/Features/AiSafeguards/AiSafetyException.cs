using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.AiSafeguards;

public class AiSafetyException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;
    public static string MessageFor(string code) => code switch
    {
        LlmErrorCodes.InvalidRequestId => "Supply a nonempty request UUID.",
        LlmErrorCodes.RequestConflict => "This request ID was already used for different input.",
        LlmErrorCodes.OperationInProgress => "This AI operation is already running.",
        LlmErrorCodes.AiBusy => "Another AI operation is running. Wait for it to finish.",
        LlmErrorCodes.ResultUnavailable => "This operation already finished. Its preview is unavailable; explicitly start a new attempt if needed.",
        LlmErrorCodes.OperationInterrupted => "This operation was interrupted. Review its outcome before explicitly starting a new attempt.",
        LlmErrorCodes.DailyLimitReached => "Your personal-provider daily call limit has been reached. Change the limit or wait for its reset.",
        LlmErrorCodes.Forbidden => "Only an Owner can manage personal-provider call limits.",
        _ => "Choose a supported provider and a nonnegative daily limit, or null to disable it.",
    };
}
