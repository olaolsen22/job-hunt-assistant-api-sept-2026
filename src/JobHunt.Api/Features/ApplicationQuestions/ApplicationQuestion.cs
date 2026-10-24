using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.ApplicationQuestions;

// A text box on an application form: "Søknadstekst", "Why do you want to work here?". A one-box cover letter is
// just a question with a limit. Ordered by Id, the order the user entered them from the form.
public class ApplicationQuestion
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public string Question { get; set; } = null!;
    // The form's character limit, whitespace included. Stored because models can't count characters:
    // the server measures every answer against it.
    public int? CharLimit { get; set; }
    public string? Answer { get; set; }
    // The user's instructions for this answer. Input for the AI, never part of the answer.
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ApplicationQuestionConfiguration : IEntityTypeConfiguration<ApplicationQuestion>
{
    public void Configure(EntityTypeBuilder<ApplicationQuestion> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(q => q.Listing).WithMany(l => l.ApplicationQuestions).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(q => q.UserId);
    }
}

[ObjectType<ApplicationQuestion>]
public static partial class ApplicationQuestionNode
{
    static partial void Configure(IObjectTypeDescriptor<ApplicationQuestion> descriptor)
    {
        descriptor.Ignore(q => q.UserId);
    }
}
