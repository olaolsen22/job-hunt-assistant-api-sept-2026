using JobHunt.Api.Data;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.LlmModels;

// Picker metadata carries attribution, never credentials or provider endpoints.
public record LlmModel(string Id, string Label, string Provider, string ModelId, LlmKeySource KeySource, string CredentialId);

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
        return new([.. (await models.AllowedModelsAsync(db, ct)).Select(m => new LlmModel(m.Id, m.Label, m.Provider, m.ModelId, m.KeySource, m.CredentialId))], effective.Id);
    }
}
