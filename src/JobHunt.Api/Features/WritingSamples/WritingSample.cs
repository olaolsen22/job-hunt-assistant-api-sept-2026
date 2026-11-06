using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.WritingSamples;

public enum WritingSampleKind { CoverLetter, Other }

// The user's own texts, sent whole to the cover letter prompt so the model can imitate their style.
public class WritingSample
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Text { get; set; } = null!;
    public Language Language { get; set; }
    public WritingSampleKind Kind { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Best fit first: language, then cover letters, then newest. Whole samples only: a cut-off one teaches a broken ending.
    public static List<WritingSample> ForPrompt(IEnumerable<WritingSample> samples, Language language, int budget)
    {
        var selected = new List<WritingSample>();
        foreach (var sample in samples
            .OrderBy(s => s.Language != language)
            .ThenBy(s => s.Kind != WritingSampleKind.CoverLetter)
            .ThenByDescending(s => s.UpdatedAt))
        {
            if (sample.Text.Length > budget) continue;
            selected.Add(sample);
            budget -= sample.Text.Length;
        }
        return selected;
    }
}

public class WritingSampleConfiguration : IEntityTypeConfiguration<WritingSample>
{
    public void Configure(EntityTypeBuilder<WritingSample> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.UserId);
    }
}
