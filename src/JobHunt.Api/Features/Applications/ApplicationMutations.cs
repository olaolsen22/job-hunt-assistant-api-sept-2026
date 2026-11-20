using System.Diagnostics;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Applications;

public class InvalidStatusTransitionException(string message) : Exception(message);

public class InvalidApplicationException(string message) : Exception(message);

public class StatusChangeNotFoundException(int id) : Exception($"Status change {id} was not found.");

[MutationType]
public static class ApplicationMutations
{
    // Interviewing is reached through addInterview; the outcomes through recordOutcome.
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Allowed = new()
    {
        [ApplicationStatus.Saved] = [ApplicationStatus.Applied],
        [ApplicationStatus.Applied] =
            [ApplicationStatus.Interviewing, ApplicationStatus.Offer, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn],
        [ApplicationStatus.Interviewing] = [ApplicationStatus.Offer, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn],
        // Rejected after an offer: the company withdrew it. Withdrawn: the user declined it.
        [ApplicationStatus.Offer] = [ApplicationStatus.Accepted, ApplicationStatus.Rejected, ApplicationStatus.Withdrawn],
    };

    // Documents are optional: form-only portals send no file, and old applications may have lost their CV.
    // Upload first with uploadDocument when there is one.
    [Error<ListingNotFoundException>]
    [Error<InvalidStatusTransitionException>]
    [Error<InvalidApplicationException>]
    [Error<DocumentNotFoundException>]
    [Error<InvalidDocumentException>]
    public static async Task<Listing> ApplyToListingAsync(
        int listingId, int? cvDocumentId, int? coverLetterDocumentId, DateTimeOffset? appliedAt, string? note,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await ListingMutations.FindWithDetailsAsync(listingId, db, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var change = ChangeStatus(listing, ApplicationStatus.Applied, appliedAt?.UtcDateTime ?? now, note, now);
        if (cvDocumentId is not null)
            Send(listing, cvDocumentId.Value, DocumentKind.Cv, change.OccurredAt);
        if (coverLetterDocumentId is not null)
            Send(listing, coverLetterDocumentId.Value, DocumentKind.CoverLetter, change.OccurredAt);
        listing.ShortlistedAt = null;
        await db.SaveChangesAsync(ct);
        return listing;
    }

    [Error<ListingNotFoundException>]
    [Error<InvalidStatusTransitionException>]
    [Error<InvalidApplicationException>]
    public static async Task<Listing> RecordOutcomeAsync(
        int listingId, ApplicationOutcome outcome, DateTimeOffset? occurredAt, string? note,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await ListingMutations.FindWithDetailsAsync(listingId, db, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var status = outcome switch
        {
            ApplicationOutcome.Offer => ApplicationStatus.Offer,
            ApplicationOutcome.Accepted => ApplicationStatus.Accepted,
            ApplicationOutcome.Rejected => ApplicationStatus.Rejected,
            ApplicationOutcome.Withdrawn => ApplicationStatus.Withdrawn,
            // Required for enums (any int is a valid value). Throwing so a new outcome can't silently map to one of these.
            _ => throw new UnreachableException(),
        };
        ChangeStatus(listing, status, occurredAt?.UtcDateTime ?? now, note, now);
        await db.SaveChangesAsync(ct);
        return listing;
    }

    // For fixing a date or note. The status itself is changed by undoing and redoing the step.
    [Error<StatusChangeNotFoundException>]
    [Error<InvalidApplicationException>]
    public static async Task<Listing> UpdateStatusChangeAsync(
        int id, DateTimeOffset occurredAt, string? note, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listingId = await db.StatusChanges.Where(s => s.Id == id).Select(s => (int?)s.ListingId).FirstOrDefaultAsync(ct)
            ?? throw new StatusChangeNotFoundException(id);
        var listing = await ListingMutations.FindWithDetailsAsync(listingId, db, ct);
        var change = listing.StatusHistory.Single(s => s.Id == id);
        change.OccurredAt = occurredAt.UtcDateTime;
        change.Note = Inputs.Clean(note);
        Validate(listing, change, time.GetUtcNow().UtcDateTime);
        if (change.Status == ApplicationStatus.Applied)
            foreach (var document in listing.Documents.Where(d => d.SentAt is not null))
                MarkSent(listing, document, change.OccurredAt);
        listing.StatusChangedAt = listing.StatusHistory.MaxBy(s => s.Id)!.OccurredAt;
        await db.SaveChangesAsync(ct);
        return listing;
    }

    // Removes the latest step, for mistakes. Undoing Applied unmarks the sent documents so they can be replaced.
    [Error<ListingNotFoundException>]
    [Error<InvalidStatusTransitionException>]
    [Error<DuplicateListingException>]
    public static async Task<Listing> UndoStatusChangeAsync(int listingId, AppDbContext db, CancellationToken ct)
    {
        var listing = await ListingMutations.FindWithDetailsAsync(listingId, db, ct);
        var last = listing.StatusHistory.MaxBy(s => s.Id)
            ?? throw new InvalidStatusTransitionException("There is nothing to undo.");
        if (last.Status == ApplicationStatus.Interviewing && listing.Interviews.Count > 0)
            throw new InvalidStatusTransitionException("Delete the interviews first.");
        // Undoing a final step reopens the listing, and only one attempt per URL may be open.
        if (ApplicationStatuses.Final.Contains(last.Status)
            && await ListingMutations.OpenListingIdWithUrlAsync(listing, db, ct) is { } openId)
            throw new DuplicateListingException(openId);

        if (last.Status == ApplicationStatus.Applied)
            foreach (var document in listing.Documents.Where(d => d.SentAt is not null))
            {
                document.SentAt = null;
                document.FileName = DocumentMutations.FileName(listing.Company.Name,
                    DocumentMutations.RoleName(listing.PostingLanguage, listing.Title, listing.JobRole.Name),
                    document.Kind, document.CreatedAt);
            }
        listing.StatusHistory.Remove(last);
        db.StatusChanges.Remove(last);
        var previous = listing.StatusHistory.MaxBy(s => s.Id);
        listing.Status = previous?.Status ?? ApplicationStatus.Saved;
        listing.StatusChangedAt = previous?.OccurredAt;
        await db.SaveChangesAsync(ct);
        return listing;
    }

    // Shared with addInterview. Expects the listing's StatusHistory to be loaded.
    internal static StatusChange ChangeStatus(
        Listing listing, ApplicationStatus status, DateTime occurredAt, string? note, DateTime now)
    {
        if (!Allowed.GetValueOrDefault(listing.Status, []).Contains(status))
            throw new InvalidStatusTransitionException($"A listing can't go from {listing.Status} to {status}.");
        var change = new StatusChange
        {
            UserId = listing.UserId,
            Status = status,
            OccurredAt = occurredAt,
            Note = Inputs.Clean(note),
            CreatedAt = now,
        };
        listing.StatusHistory.Add(change);
        Validate(listing, change, now);
        listing.Status = status;
        listing.StatusChangedAt = occurredAt;
        return change;
    }

    private static void Validate(Listing listing, StatusChange change, DateTime now)
    {
        if (change.OccurredAt > now)
            throw new InvalidApplicationException("The date can't be in the future.");
        if (change.Note?.Length > Inputs.MaxMarkdownLength)
            throw new InvalidApplicationException($"The note is longer than {Inputs.MaxMarkdownLength} characters.");
        var appliedAt = listing.StatusHistory.Single(s => s.Status == ApplicationStatus.Applied).OccurredAt;
        if (listing.StatusHistory.Any(s => s.OccurredAt < appliedAt))
            throw new InvalidApplicationException("Nothing can happen before the application date.");
    }

    private static void Send(Listing listing, int documentId, DocumentKind kind, DateTime sentAt)
    {
        var document = listing.Documents.FirstOrDefault(d => d.Id == documentId)
            ?? throw new DocumentNotFoundException(documentId);
        if (document.Kind != kind)
            throw new InvalidDocumentException($"Document {documentId} is not a {kind}.");
        MarkSent(listing, document, sentAt);
    }

    private static void MarkSent(Listing listing, Document document, DateTime sentAt)
    {
        document.SentAt = sentAt;
        document.FileName = DocumentMutations.FileName(listing.Company.Name,
            DocumentMutations.RoleName(listing.PostingLanguage, listing.Title, listing.JobRole.Name), document.Kind, sentAt);
    }
}

[ObjectType<StatusChange>]
public static partial class StatusChangeNode
{
    static partial void Configure(IObjectTypeDescriptor<StatusChange> descriptor)
    {
        descriptor.Ignore(s => s.UserId);
    }
}
