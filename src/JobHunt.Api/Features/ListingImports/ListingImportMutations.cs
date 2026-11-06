using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.ListingImports;

public class ListingImportNotFoundException(int id) : Exception($"Listing import {id} was not found.");

[MutationType]
public static class ListingImportMutations
{
    [Error<ListingImportNotFoundException>]
    public static async Task<ListingImport> SetListingImportReviewedAsync(
        int id, bool isReviewed, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        // The user filter applies here too, so another user's import counts as not found.
        var entity = await db.ListingImports.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new ListingImportNotFoundException(id);
        // ??= keeps the first review time when marking twice.
        if (isReviewed)
            entity.ReviewedAt ??= time.GetUtcNow().UtcDateTime;
        else
            entity.ReviewedAt = null;
        await db.SaveChangesAsync(ct);
        return entity;
    }
}
