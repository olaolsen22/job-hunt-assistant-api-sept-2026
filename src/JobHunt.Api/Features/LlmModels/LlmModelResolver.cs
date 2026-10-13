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
public class LlmModelResolver(IOptions<GeminiOptions> gemini, IHttpContextAccessor http)
{
    public const int MaxModelLength = 100;

    // Anyone who isn't the Owner gets only the default (least privilege), so a new role can't reach paid models.
    public IReadOnlyList<LlmModelOption> AllowedModels() =>
        http.HttpContext?.User.IsInRole(Roles.Owner) == true ? gemini.Value.Models : [gemini.Value.GetDefault()];

    // The user's stored default when it's still allowed, else the configured one.
    public async Task<LlmModelOption> GetDefaultAsync(AppDbContext db, CancellationToken ct)
    {
        var stored = await db.UserLlmSettings.Select(s => s.DefaultModel).FirstOrDefaultAsync(ct);
        return Find(stored) ?? gemini.Value.GetDefault();
    }

    // Requested model, else the user's default. Ids match exactly (ordinal), so an oversized or differently cased
    // id is simply unknown.
    public async Task<LlmModelOption> ResolveAsync(string? requested, AppDbContext db, CancellationToken ct) =>
        requested is null
            ? await GetDefaultAsync(db, ct)
            : Find(requested) ?? throw new UnknownLlmModelException();

    private LlmModelOption? Find(string? id) => id is null ? null : AllowedModels().FirstOrDefault(m => m.Id == id);
}
