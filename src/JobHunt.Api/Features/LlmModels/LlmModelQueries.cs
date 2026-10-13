using JobHunt.Api.Data;

namespace JobHunt.Api.Features.LlmModels;

// Only id and label: limits are on llmUsageToday, and nothing provider-side (keys, endpoints) ever leaves the API.
public record LlmModel(string Id, string Label);

public record LlmModels(List<LlmModel> Models, string DefaultModel);

[QueryType]
public static class LlmModelQueries
{
    // Per user: the Guest gets a one-item list, so the UI renders the same picker for both accounts.
    public static Task<LlmModels> GetLlmModelsAsync(AppDbContext db, LlmModelResolver models, CancellationToken ct) =>
        LoadAsync(db, models, ct);

    internal static async Task<LlmModels> LoadAsync(AppDbContext db, LlmModelResolver models, CancellationToken ct)
    {
        var effective = await models.GetDefaultAsync(db, ct);
        return new([.. models.AllowedModels().Select(m => new LlmModel(m.Id, m.Label))], effective.Id);
    }
}
