using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Applications;

public class InvalidInterviewException(string message) : Exception(message);

public class InterviewNotFoundException(int id) : Exception($"Interview {id} was not found.");

[MutationType]
public static class InterviewMutations
{
    private const int MaxTitleLength = 200;

    // The first interview moves an Applied listing to Interviewing, dated now: that's when the invite was logged.
    [Error<ListingNotFoundException>]
    [Error<InvalidStatusTransitionException>]
    [Error<InvalidInterviewException>]
    public static async Task<Interview> AddInterviewAsync(
        int listingId, DateTimeOffset scheduledAt, string? title, string? notes,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings.Include(l => l.StatusHistory).FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new ListingNotFoundException(listingId);
        if (listing.Status is not (ApplicationStatus.Applied or ApplicationStatus.Interviewing))
            throw new InvalidStatusTransitionException($"Interviews can't be added to a listing that is {listing.Status}.");

        var now = time.GetUtcNow().UtcDateTime;
        var interview = new Interview { UserId = listing.UserId, CreatedAt = now };
        Apply(interview, scheduledAt, title, notes, now);
        if (listing.Status == ApplicationStatus.Applied)
            ApplicationMutations.ChangeStatus(listing, ApplicationStatus.Interviewing, now, null, now);
        listing.Interviews.Add(interview);
        await db.SaveChangesAsync(ct);
        return interview;
    }

    // Allowed after an outcome too, to add notes in hindsight.
    [Error<InterviewNotFoundException>]
    [Error<InvalidInterviewException>]
    public static async Task<Interview> UpdateInterviewAsync(
        int id, DateTimeOffset scheduledAt, string? title, string? notes,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var interview = await db.Interviews.Include(i => i.Listing).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new InterviewNotFoundException(id);
        Apply(interview, scheduledAt, title, notes, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return interview;
    }

    [Error<InterviewNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteInterviewAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.Interviews.Where(i => i.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new InterviewNotFoundException(id);
        return id;
    }

    private static void Apply(Interview interview, DateTimeOffset scheduledAt, string? title, string? notes, DateTime now)
    {
        interview.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        interview.Notes = Inputs.Clean(notes);
        if (interview.Title?.Length > MaxTitleLength)
            throw new InvalidInterviewException($"The title is longer than {MaxTitleLength} characters.");
        if (interview.Notes?.Length > Inputs.MaxMarkdownLength)
            throw new InvalidInterviewException($"The notes are longer than {Inputs.MaxMarkdownLength} characters.");
        interview.ScheduledAt = scheduledAt.UtcDateTime;
        interview.UpdatedAt = now;
    }
}

[ObjectType<Interview>]
public static partial class InterviewNode
{
    static partial void Configure(IObjectTypeDescriptor<Interview> descriptor)
    {
        descriptor.Ignore(i => i.UserId);
    }
}
