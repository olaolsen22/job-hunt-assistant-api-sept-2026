using System.ComponentModel.DataAnnotations;

namespace JobHunt.Api.Llm;

public class GeminiOptions
{
    public const string Section = "Llm:Gemini";

    // Keyed-service name, also stored on jobs and usage rows so stats can group by provider.
    public const string Provider = "gemini";

    // From user-secrets (dev) or the Llm__Gemini__ApiKey env var (Docker); never in appsettings.
    [Required]
    public string ApiKey { get; set; } = "";

    [Required]
    public string Model { get; set; } = "";

    // Copied by hand from AI Studio's rate-limit page: Gemini has no API for its limits. Per model, so it
    // changes with Model. Unset means the usage widget shows no limit.
    public int? RequestsPerDay { get; set; }
}
