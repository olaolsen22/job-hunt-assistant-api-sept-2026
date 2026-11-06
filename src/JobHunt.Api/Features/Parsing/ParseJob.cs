using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Parsing;

// Full = one ad into the listing form; Quick = several ads into the quick-add table;
// Companies = a list of names or text about companies into company rows.
public enum ParseMode { Full, Quick, Companies }

// Queued and Retrying return with the background queue; until then jobs finish inside the mutation.
public enum ParseJobStatus { /* Queued, */ Running, /* Retrying, */ Succeeded, Failed }

public class ParseJob
{
    // A Guid, not an int, so the id exists before the row is saved.
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public ParseMode Mode { get; set; }
    public ParseJobStatus Status { get; set; }
    // The text the model was given.
    public string RawText { get; set; } = null!;
    // JSON text in the shape the copy-paste path produces, so the UI handles both paths the same way.
    public string? Result { get; set; }
    // Shown to the user as is.
    public string? Error { get; set; }
    public string Provider { get; set; } = null!;
    // The model used; null on jobs from before model selection.
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ParseJobConfiguration : IEntityTypeConfiguration<ParseJob>
{
    public void Configure(EntityTypeBuilder<ParseJob> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(j => j.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

[ObjectType<ParseJob>]
public static partial class ParseJobNode
{
    static partial void Configure(IObjectTypeDescriptor<ParseJob> descriptor)
    {
        descriptor.Ignore(j => j.UserId);
        descriptor.Ignore(j => j.RawText);
    }
}
