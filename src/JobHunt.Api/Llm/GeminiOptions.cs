using System.ComponentModel.DataAnnotations;

namespace JobHunt.Api.Llm;

public class GeminiOptions : IValidatableObject
{
    public const string Section = "Llm:Gemini";

    // Keyed-service name, also stored on jobs and usage rows so stats can group by provider.
    public const string Provider = "gemini";

    // From user-secrets (dev) or the Llm__Gemini__ApiKey env var (Docker); never in appsettings.
    [Required]
    public string ApiKey { get; set; } = "";

    // Used when the user has no default of their own, and the only model a Guest gets.
    [Required]
    public string DefaultModel { get; set; } = "";

    // The allowlist: a model id from the client is only ever sent to Gemini if it's in here.
    [MinLength(1)]
    public List<LlmModelOption> Models { get; set; } = [];

    // A method, not a property: the DataAnnotations validator reads every property, and this throws on a bad config.
    public LlmModelOption GetDefault() => Models.First(m => m.Id == DefaultModel);

    // DataAnnotations doesn't validate list items, so the per-model checks live here.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Models.Any(m => string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Label)))
            yield return new ValidationResult("Every Llm:Gemini:Models entry needs an Id and a Label.");
        if (Models.DistinctBy(m => m.Id).Count() != Models.Count)
            yield return new ValidationResult("Llm:Gemini:Models has duplicate ids.");
        if (Models.All(m => m.Id != DefaultModel))
            yield return new ValidationResult("Llm:Gemini:DefaultModel must be one of Llm:Gemini:Models.");
    }
}

public class LlmModelOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";

    // Copied by hand from AI Studio's rate-limit page: Gemini has no API for its limits. Unset means the usage
    // widget shows no limit.
    public int? RequestsPerDay { get; set; }
}
