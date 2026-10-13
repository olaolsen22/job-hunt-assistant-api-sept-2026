using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobHunt.Api.Features.LlmModels;

// Fixed message: echoing the submitted id back would only help someone probing for model names.
public class UnknownLlmModelException() : Exception("That model isn't available.");

// The only gate between a client-sent model id and a paid API call, so the allowlist is computed here from the
// role, never trusted from the UI.
public class LlmModelResolver(IOptions<GeminiOptions> gemini, IOptions<ProviderCatalogOptions> catalog, IHttpContextAccessor http)
{
    public const int MaxModelLength = 100;

    public async Task<IReadOnlyList<ResolvedLlmModel>> AllowedModelsAsync(AppDbContext db, CancellationToken ct)
    {
        var owner = http.HttpContext?.User.IsInRole(Roles.Owner) == true;
        var keys = owner
            ? await db.UserProviderKeys.Where(k => k.RetiredAt == null).Select(k => new { k.Id, k.Provider }).ToListAsync(ct)
            : [];
        var geminiKey = keys.SingleOrDefault(k => k.Provider == GeminiOptions.Provider)?.Id;
        var configured = owner ? gemini.Value.Models : [gemini.Value.GetDefault()];
        List<ResolvedLlmModel> allowed = [.. configured.Select(m =>
            new ResolvedLlmModel(m.Id, GeminiOptions.Provider, m.Id, m.Label, geminiKey,
                geminiKey is null ? m.RequestsPerDay : null))];
        foreach (var key in keys.Where(k => k.Provider != GeminiOptions.Provider))
            allowed.AddRange(catalog.Value.For(key.Provider).Select(m =>
                new ResolvedLlmModel($"{key.Provider}:{m.Id}", key.Provider, m.Id, m.Label, key.Id)));
        return allowed;
    }

    public async Task<ResolvedLlmModel> GetDefaultAsync(AppDbContext db, CancellationToken ct)
    {
        var allowed = await AllowedModelsAsync(db, ct);
        var stored = await db.UserLlmSettings.Select(s => s.DefaultModel).FirstOrDefaultAsync(ct);
        return allowed.FirstOrDefault(m => m.Id == stored) ?? allowed.First(m => m.Id == gemini.Value.DefaultModel);
    }

    public async Task<ResolvedLlmModel> ResolveAsync(string? requested, AppDbContext db, CancellationToken ct) =>
        requested is null
            ? await GetDefaultAsync(db, ct)
            : (await AllowedModelsAsync(db, ct)).FirstOrDefault(m => m.Id == requested) ?? throw new UnknownLlmModelException();
}
