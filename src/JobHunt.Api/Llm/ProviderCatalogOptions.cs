using System.ComponentModel.DataAnnotations;
using JobHunt.Api.Features.LlmModels;

namespace JobHunt.Api.Llm;

public class ProviderCatalogOptions : IValidatableObject
{
    public const string Section = "Llm:Providers";
    public List<ProviderModelOption> OpenAI { get; set; } = [];
    public List<ProviderModelOption> Mistral { get; set; } = [];

    public IReadOnlyList<ProviderModelOption> For(string provider) => provider switch
    { LlmProviders.OpenAI => OpenAI, LlmProviders.Mistral => Mistral, _ => [] };

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var provider in LlmProviders.Supported.Where(p => p != LlmProviders.Gemini))
        {
            var models = For(provider);
            if (models.Count == 0 || models.Any(m => string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Label)
                || $"{provider}:{m.Id}".Length > LlmModelResolver.MaxModelLength || m.Id.Any(char.IsControl)
                || m.MaxOutputTokens < 32768 || m.ContextWindowTokens < 150000)
                || models.DistinctBy(m => m.Id).Count() != models.Count)
                yield return new("Provider catalogs require unique labeled text models supporting the parsing budgets.");
        }
    }

    public bool HasUniqueSelectionIds(GeminiOptions gemini)
    {
        var ids = gemini.Models.Select(m => m.Id).Concat(LlmProviders.Supported.Where(p => p != LlmProviders.Gemini)
            .SelectMany(p => For(p).Select(m => $"{p}:{m.Id}"))).ToList();
        return ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
    }
}

public class ProviderModelOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public int MaxOutputTokens { get; set; }
    public int ContextWindowTokens { get; set; }
}
