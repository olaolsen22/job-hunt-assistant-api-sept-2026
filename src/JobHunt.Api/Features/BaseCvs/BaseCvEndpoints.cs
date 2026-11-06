using JobHunt.Api.Data;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.BaseCvs;

// REST, like document download: the browser needs a plain URL it can open with the auth cookie, and GraphQL answers
// in JSON.
public static class BaseCvEndpoints
{
    public static void MapBaseCvEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/base-cv-versions");

        group.MapGet("/{id:int}/pdf", Preview);
    }

    // Rendered on request and never stored: only a document generated on a listing records an application.
    private static async Task<Results<FileContentHttpResult, NotFound>> Preview(
        int id, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        // The user filter applies, so another user's version is a 404.
        var version = await db.BaseCvVersions.AsNoTracking().Include(v => v.BaseCv)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (version is null)
            return TypedResults.NotFound();
        var bytes = await TailoredCvDraftMutations.RenderPdfAsync(version.Content, db, ct);

        var language = version.Language == Language.Norwegian ? "NO" : "EN";
        var fileName = $"{DocumentMutations.Slug(version.BaseCv.Name)}_{language}_v{version.Number}.pdf";
        return DocumentEndpoints.InlinePdf(context, bytes, fileName);
    }
}
