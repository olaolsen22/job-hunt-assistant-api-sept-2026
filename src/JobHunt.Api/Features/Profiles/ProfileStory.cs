using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

// Guided sections, so the cover letter prompt can tell origin from motivation. Stored as Story_* columns.
public class ProfileStory
{
    public string? Origin { get; set; }
    public string? Upbringing { get; set; }
    public string? CareerJourney { get; set; }
    public string? Motivation { get; set; }
    public string? Values { get; set; }
}

[MutationType]
public static class ProfileStoryMutations
{
    internal const int MaxLength = 2000;

    // Full replace: a section left out is cleared.
    [Error<InvalidProfileException>]
    public static async Task<Profile> UpdateProfileStoryAsync(string? origin, string? upbringing,
        string? careerJourney, string? motivation, string? values, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        profile.Story = new ProfileStory
        {
            Origin = ProfileMutations.Text(origin, "Origin", MaxLength),
            Upbringing = ProfileMutations.Text(upbringing, "Upbringing", MaxLength),
            CareerJourney = ProfileMutations.Text(careerJourney, "Career journey", MaxLength),
            Motivation = ProfileMutations.Text(motivation, "Motivation", MaxLength),
            Values = ProfileMutations.Text(values, "Values", MaxLength),
        };
        await db.SaveChangesAsync(ct);
        return await ProfileQueries.LoadAsync(db, ct);
    }
}
