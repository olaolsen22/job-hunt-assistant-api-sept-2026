using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.ApplicationQuestions;

public class ApplicationQuestionNotFoundException(int id) : Exception($"Application question {id} was not found.");

public class InvalidApplicationQuestionException(string message) : Exception(message);

[MutationType]
public static class ApplicationQuestionMutations
{
    private const int MaxQuestionsPerListing = 20;

    [Error<ListingNotFoundException>]
    [Error<InvalidApplicationQuestionException>]
    public static async Task<ApplicationQuestion> AddApplicationQuestionAsync(int listingId, string question,
        int? charLimit, string? answer, string? notes, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == listingId, ct))
            throw new ListingNotFoundException(listingId);
        if (await db.ApplicationQuestions.CountAsync(q => q.ListingId == listingId, ct) >= MaxQuestionsPerListing)
            throw new InvalidApplicationQuestionException($"A listing can have at most {MaxQuestionsPerListing} questions.");

        var now = time.GetUtcNow().UtcDateTime;
        var entity = new ApplicationQuestion { UserId = db.CurrentUserId!, ListingId = listingId, CreatedAt = now };
        ApplicationQuestionValidation.Apply(entity, question, charLimit, answer, notes, now);
        db.ApplicationQuestions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    // Full replace; also how a reviewed AI candidate is saved.
    [Error<ApplicationQuestionNotFoundException>]
    [Error<InvalidApplicationQuestionException>]
    public static async Task<ApplicationQuestion> UpdateApplicationQuestionAsync(int id, string question,
        int? charLimit, string? answer, string? notes, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var entity = await db.ApplicationQuestions.FirstOrDefaultAsync(q => q.Id == id, ct)
            ?? throw new ApplicationQuestionNotFoundException(id);
        ApplicationQuestionValidation.Apply(entity, question, charLimit, answer, notes, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    [Error<ApplicationQuestionNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteApplicationQuestionAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ApplicationQuestions.Where(q => q.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ApplicationQuestionNotFoundException(id);
        return id;
    }
}

internal static class ApplicationQuestionValidation
{
    private const int MaxQuestionLength = 1000;
    private const int MaxCharLimit = 10_000;
    private const int MaxNotesLength = 2000;

    // A saved answer must fit the form: candidates may run over, but the server won't store one that does.
    public static void Apply(ApplicationQuestion entity, string question, int? charLimit, string? answer,
        string? notes, DateTime now)
    {
        if (charLimit is < 1 or > MaxCharLimit)
            throw new InvalidApplicationQuestionException($"charLimit: between 1 and {MaxCharLimit}.");
        entity.Question = Text(question, "question", MaxQuestionLength)
            ?? throw new InvalidApplicationQuestionException("question: required.");
        entity.CharLimit = charLimit;
        entity.Answer = Text(answer, "answer", charLimit ?? MaxCharLimit);
        entity.Notes = Text(notes, "notes", MaxNotesLength);
        entity.UpdatedAt = now;
    }

    // Line breaks count as one character, as in the browser's textarea value and maxlength.
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace("\r\n", "\n").Trim();

    private static string? Text(string? value, string field, int maxLength)
    {
        var text = Normalize(value);
        if (text?.Length > maxLength)
            throw new InvalidApplicationQuestionException($"{field}: at most {maxLength} characters (now {text.Length}).");
        return text;
    }
}
