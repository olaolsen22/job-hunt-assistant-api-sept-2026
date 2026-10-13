namespace JobHunt.Api.Features.AiSafeguards;

public class AiSafetyException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;
    public static string MessageFor(string code) => code switch
    {
        "invalid-request-id" => "Supply a nonempty request UUID.",
        "request-conflict" => "This request ID was already used for different input.",
        "operation-in-progress" => "This AI operation is already running.",
        "ai-busy" => "Another AI operation is running. Wait for it to finish.",
        "result-unavailable" => "This operation already finished. Its preview is unavailable; explicitly start a new attempt if needed.",
        "operation-interrupted" => "This operation was interrupted. Review its outcome before explicitly starting a new attempt.",
        "daily-limit-reached" => "Your personal-provider daily call limit has been reached. Change the limit or wait for its reset.",
        "forbidden" => "Only an Owner can manage personal-provider call limits.",
        _ => "Choose a supported provider and a nonnegative daily limit, or null to disable it.",
    };
}
