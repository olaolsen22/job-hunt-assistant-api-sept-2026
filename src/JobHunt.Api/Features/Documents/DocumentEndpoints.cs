using System.Net.Http.Headers;
using JobHunt.Api.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Documents;

// REST, not GraphQL: GraphQL answers in JSON, and the browser needs a plain URL it can open or download
// with the auth cookie. Upload, listing and deleting documents stay in GraphQL.
public static class DocumentEndpoints
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

        // Inline opens the PDF in a browser tab; saving it (or an <a download>) still uses the generated name.
        context.Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(file.Bytes, "application/pdf");
    }
}
