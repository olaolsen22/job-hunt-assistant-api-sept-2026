using System.Text.RegularExpressions;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Documents;

public class InvalidDocumentException(string message) : Exception(message);

public class DocumentNotFoundException(int id) : Exception($"Document {id} was not found.");

public class DocumentSentException(int id) : Exception($"Document {id} has been sent and can't be deleted. Unmark it as sent first.");

[MutationType]
public static partial class DocumentMutations
{
    private const long MaxBytes = 5 * 1024 * 1024;

    [Error<ListingNotFoundException>]
    [Error<InvalidDocumentException>]
    public static async Task<Document> UploadDocumentAsync(
        int listingId, DocumentKind kind, IFile file, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => new { Company = l.Company.Name, JobRole = l.JobRole.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);

        // Length can be unknown up front, so check the bytes actually read too.
        if (file.Length > MaxBytes)
            throw new InvalidDocumentException("File is larger than 5 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length > MaxBytes)
            throw new InvalidDocumentException("File is larger than 5 MB.");
        // Checks the content itself; the client's content type and file extension can say anything.
        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDocumentException("Only PDF files are allowed.");

        var now = time.GetUtcNow().UtcDateTime;
        var document = new Document
        {
            UserId = db.CurrentUserId!,
            ListingId = listingId,
            Kind = kind,
            FileName = $"{Slug(listing.Company)}_{Slug(listing.JobRole)}_{kind}_{now:yyyy-MM-dd}.pdf",
            OriginalFileName = file.Name,
            SizeBytes = bytes.Length,
            CreatedAt = now,
        };
        db.Add(new DocumentContent { Document = document, Bytes = bytes });
        await db.SaveChangesAsync(ct);
        return document;
    }

    [Error<DocumentNotFoundException>]
    public static async Task<Document> SetDocumentSentAsync(
        int id, bool isSent, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DocumentNotFoundException(id);
        // ??= keeps the original time when marking twice.
        if (isSent)
            document.SentAt ??= time.GetUtcNow().UtcDateTime;
        else
            document.SentAt = null;
        await db.SaveChangesAsync(ct);
        return document;
    }

    [Error<DocumentNotFoundException>]
    [Error<DocumentSentException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteDocumentAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var sentAt = await db.Documents.Where(d => d.Id == id).Select(d => new { d.SentAt }).FirstOrDefaultAsync(ct)
            ?? throw new DocumentNotFoundException(id);
        if (sentAt.SentAt is not null)
            throw new DocumentSentException(id);
        await db.Documents.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        return id;
    }

    // Letters and digits only (æøå included), so the name is safe on any OS: "Acme AS" → "Acme-AS".
    private static string Slug(string name) => NonAlphanumeric().Replace(name, "-").Trim('-');

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();
}

[ObjectType<Document>]
public static partial class DocumentNode
{
    static partial void Configure(IObjectTypeDescriptor<Document> descriptor)
    {
        descriptor.Ignore(d => d.UserId);
    }
}
