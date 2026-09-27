using JobHunt.Api.Data;

namespace JobHunt.Api.Features.Parsing;

[QueryType]
public static class ParseQueries
{
    // Lets the UI catch up on a job whose subscription events it missed.
    [UseFirstOrDefault]
    [UseProjection]
    public static IQueryable<ParseJob> GetParseJob(Guid id, AppDbContext db) =>
        db.ParseJobs.Where(j => j.Id == id);
}
