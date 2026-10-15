using System.Text.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.CoverLetters;

// One editable letter per listing, like the tailored CV draft.
public class CoverLetterDraft
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    // Leaf data: the UI edits it as a whole, and no query looks inside it.
    public string ContentJson { get; set; } = "{}";
    // The user's notes for this application (why this company, what to stress). Input for the AI, never printed.
    public string? Brief { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public CoverLetterContent Content
    {
        get => JsonSerializer.Deserialize<CoverLetterContent>(ContentJson, TailoredCvJson.Options) ?? new();
        set => ContentJson = JsonSerializer.Serialize(value, TailoredCvJson.Options);
    }
}

// Mirrors the traditional Norwegian letter (docs/norwegian-cover-letter.md): sender block with the date below it,
// recipient block, bold subject, body, sign-off and name. FullName is both the sender line and the signature.
// Address and RecipientAddress may span lines.
public record CoverLetterContent(
    Language Language = Language.English,
    string? FullName = null,
    string? Address = null,
    string? Phone = null,
    string? Email = null,
    DateOnly? Date = null,
    string? Organization = null,
    string? RecipientName = null,
    string? RecipientAddress = null,
    string? Subject = null,
    string? Greeting = null,
    List<string>? Paragraphs = null,
    string? Closing = null)
{
    // Never null, so old or empty JSON still reads as a letter with no paragraphs.
    public List<string> Paragraphs { get; init; } = Paragraphs ?? [];
}

public class CoverLetterDraftConfiguration : IEntityTypeConfiguration<CoverLetterDraft>
{
    public void Configure(EntityTypeBuilder<CoverLetterDraft> builder)
    {
        builder.Ignore(d => d.Content);
        builder.Property(d => d.ContentJson).HasColumnType("TEXT");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Listing).WithMany().HasForeignKey(d => d.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.ListingId).IsUnique();
    }
}
