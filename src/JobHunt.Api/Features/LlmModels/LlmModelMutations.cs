using JobHunt.Api.Data;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.LlmModels;

[MutationType]
public static class LlmModelMutations
{
    // Null clears the stored default. No userId argument: the query filter and CurrentUserId are the only way in.
    [Error<UnknownLlmModelException>]
    [UseMutationConvention(PayloadFieldName = "llmModels")]
    public static async Task<LlmModels> SetDefaultLlmModelAsync(
        string? model, AppDbContext db, LlmModelResolver models, CancellationToken ct)
    {
        var resolved = model is not null ? await models.ResolveAsync(model, db, ct) : null;

        var setting = await db.UserLlmSettings.FirstOrDefaultAsync(ct);
        if (setting is null)
        {
            setting = new UserLlmSetting { UserId = db.CurrentUserId!, Provider = GeminiOptions.Provider };
            db.UserLlmSettings.Add(setting);
        }
        setting.DefaultModel = model;
        setting.Provider = resolved?.Provider ?? GeminiOptions.Provider;
        await db.SaveChangesAsync(ct);
        return await LlmModelQueries.LoadAsync(db, models, ct);
    }
}
