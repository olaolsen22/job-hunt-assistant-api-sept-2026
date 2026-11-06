using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Contacts;

// One message, call or meeting with a contact: the outreach log. Looser than an application on purpose; once a
// conversation turns into a job, that job is an ordinary listing linked to the contact.
public class ContactInteraction
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    // When it happened, set by the user because it's often logged afterwards. CreatedAt is when it was logged.
    public DateTime OccurredAt { get; set; }
    public ContactDirection Direction { get; set; }
    public ContactChannel Channel { get; set; }
    // Markdown.
    public string? Note { get; set; }
    // When to chase this up. Only the latest interaction's date counts (Contact.FollowUpOn), so logging the reply
    // without one clears it.
    public DateOnly? FollowUpOn { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ContactDirection { Sent, Received }

public enum ContactChannel { Email, LinkedIn, Phone, Meeting, Other }

public class ContactInteractionConfiguration : IEntityTypeConfiguration<ContactInteraction>
{
    public void Configure(EntityTypeBuilder<ContactInteraction> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Contact).WithMany(c => c.Interactions).OnDelete(DeleteBehavior.Cascade);
    }
}
