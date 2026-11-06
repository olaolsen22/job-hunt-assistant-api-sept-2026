using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using JobHunt.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Documents;

// REST, not GraphQL: the browser needs a plain URL it can open with the auth cookie.
public static partial class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents");

        group.MapGet("/{id:int}", Download);
    }

    private static async Task<Results<FileContentHttpResult, NotFound>> Download(
        int id, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        // The user filter applies, so another user's document is a 404.
        var file = await db.DocumentContents
            .Where(c => c.DocumentId == id)
            .Select(c => new { c.Document.FileName, c.Bytes })
            .FirstOrDefaultAsync(ct);
        if (file is null)
            return TypedResults.NotFound();
        var fullName = await db.Profiles.Select(p => p.FullName).FirstOrDefaultAsync(ct);

        return InlinePdf(context, file.Bytes, DownloadName(fullName, file.FileName));
    }

    // Inline opens the PDF in a browser tab; saving it (or an <a download>) still uses the name.
    internal static FileContentHttpResult InlinePdf(HttpContext context, byte[] bytes, string fileName)
    {
        context.Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("inline") { FileNameStar = fileName }.ToString();
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(bytes, "application/pdf");
    }

    // The name a saved file gets: "Jane-Doe_Acme_Dev_Cv.pdf". The stored name's date helps in the app, but not on
    // a recruiter's disk. No profile name means no prefix.
    public static string DownloadName(string? fullName, string fileName)
    {
        var withoutDate = TrailingDate().Replace(fileName, ".pdf");
        var name = DocumentMutations.Slug(fullName ?? "");
        return name.Length == 0 ? withoutDate : $"{name}_{withoutDate}";
    }

    [GeneratedRegex(@"_\d{4}-\d{2}-\d{2}\.pdf$")]
    private static partial Regex TrailingDate();
}
