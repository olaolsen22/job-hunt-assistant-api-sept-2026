using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Samples;

public record SampleResponse(int Id, string Name);

public static class SampleEndpoints
{
    public static void MapSampleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/samples");

        group.MapGet("/", async (AppDbContext db) =>
            TypedResults.Ok(await db.Samples
                .Select(s => new SampleResponse(s.Id, s.Name))
                .ToListAsync()));
    }
}
