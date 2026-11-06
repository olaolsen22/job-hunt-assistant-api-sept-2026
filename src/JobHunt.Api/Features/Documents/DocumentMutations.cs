using System.Security.Cryptography;
using System.Text.RegularExpressions;
using JobHunt.Api.Data;
using JobHunt.Api.Features.BaseCvs;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Documents;

public class InvalidDocumentException(string message) : Exception(message);

public class DocumentNotFoundException(int id) : Exception($"Document {id} was not found.");

public class DocumentSentException(int id) : Exception($"Document {id} has been sent and can't be deleted. Undo the application first.");

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
            FileName = FileName(listing.Company, listing.JobRole, kind, now),
            OriginalFileName = file.Name,
            SizeBytes = bytes.Length,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            CreatedAt = now,
            Origin = DocumentOrigin.Uploaded,
        };
        db.Add(new DocumentContent { Document = document, Bytes = bytes });
        await db.SaveChangesAsync(ct);
        return document;
    }

    // A PDF the app rendered for a listing: named like an upload, unsent. The caller saves.
    internal static Document AddGenerated(AppDbContext db, int listingId, string company, string jobRole,
        DocumentKind kind, DocumentOrigin origin, byte[] bytes, DateTime now, int? baseCvVersionId = null)
    {
        var fileName = FileName(company, jobRole, kind, now);
        var document = new Document
        {
            UserId = db.CurrentUserId!,
            ListingId = listingId,
            Kind = kind,
            FileName = fileName,
            OriginalFileName = fileName,
            SizeBytes = bytes.Length,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            CreatedAt = now,
            Origin = origin,
            BaseCvVersionId = baseCvVersionId,
        };
        db.Add(new DocumentContent { Document = document, Bytes = bytes });
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

    // The date is the upload date, then the applied date once sent, so a downloaded file shows when it went out.
    internal static string FileName(string company, string jobRole, DocumentKind kind, DateTime date) =>
        $"{Slug(company)}_{Slug(jobRole)}_{kind}_{date:yyyy-MM-dd}.pdf";

    // Letters and digits only (æøå included), so the name is safe on any OS: "Acme AS" → "Acme-AS".
    internal static string Slug(string name) => NonAlphanumeric().Replace(name, "-").Trim('-');

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumeric();
}

[ObjectType<Document>]
public static partial class DocumentNode
{
    static partial void Configure(IObjectTypeDescriptor<Document> descriptor)
    {
        descriptor.Ignore(d => d.UserId);
        descriptor.Field(d => d.BaseCvVersionId).IsProjected();
    }

    public static async Task<BaseCvVersion?> GetBaseCvVersionAsync(
        [Parent] Document document, IBaseCvVersionByIdDataLoader versions, CancellationToken ct) =>
        document.BaseCvVersionId is { } id ? await versions.LoadAsync(id, ct) : null;
}
