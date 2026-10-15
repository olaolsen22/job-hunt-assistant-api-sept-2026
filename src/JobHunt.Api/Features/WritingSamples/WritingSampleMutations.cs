using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.WritingSamples;

public class InvalidWritingSampleException(string message) : Exception(message);
public class WritingSampleNotFoundException(int id) : Exception($"Writing sample {id} was not found.");

[QueryType]
public static class WritingSampleQueries
{
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<WritingSample> GetWritingSamples(AppDbContext db, ISortingContext sorting)
    {
        sorting.Handled(false);
        return sorting.IsDefined ? db.WritingSamples : db.WritingSamples.OrderByDescending(s => s.UpdatedAt);
    }
}

[MutationType]
public static class WritingSampleMutations
{
    internal const int MaxSamples = 20;
    private const int MaxTitleLength = 100;
    internal const int MaxTextLength = 8000;

    [Error<InvalidWritingSampleException>]
    public static async Task<WritingSample> AddWritingSampleAsync(string title, string text, Language language,
        WritingSampleKind kind, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (await db.WritingSamples.CountAsync(ct) >= MaxSamples)
            throw new InvalidWritingSampleException($"You can keep at most {MaxSamples} writing samples.");
        var now = time.GetUtcNow().UtcDateTime;
        var sample = new WritingSample { UserId = db.CurrentUserId!, CreatedAt = now };
        Apply(sample, title, text, language, kind, now);
        db.WritingSamples.Add(sample);
        await db.SaveChangesAsync(ct);
        return sample;
    }

    // Full replace.
    [Error<InvalidWritingSampleException>]
    [Error<WritingSampleNotFoundException>]
    public static async Task<WritingSample> UpdateWritingSampleAsync(int id, string title, string text,
        Language language, WritingSampleKind kind, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var sample = await db.WritingSamples.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new WritingSampleNotFoundException(id);
        Apply(sample, title, text, language, kind, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return sample;
    }

    [Error<WritingSampleNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteWritingSampleAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.WritingSamples.Where(s => s.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new WritingSampleNotFoundException(id);
        return id;
    }

    private static void Apply(WritingSample sample, string title, string text, Language language,
        WritingSampleKind kind, DateTime now)
    {
        title = title.Trim();
        text = text.Trim();
        if (title.Length is 0 or > MaxTitleLength)
            throw new InvalidWritingSampleException($"Titles must contain 1 to {MaxTitleLength} characters.");
        if (text.Length is 0 or > MaxTextLength)
            throw new InvalidWritingSampleException($"Sample text must contain 1 to {MaxTextLength} characters.");
        sample.Title = title;
        sample.Text = text;
        sample.Language = language;
        sample.Kind = kind;
        sample.UpdatedAt = now;
    }
}

[ObjectType<WritingSample>]
public static partial class WritingSampleNode
{
    static partial void Configure(IObjectTypeDescriptor<WritingSample> descriptor) =>
        descriptor.Ignore(s => s.UserId);
}
