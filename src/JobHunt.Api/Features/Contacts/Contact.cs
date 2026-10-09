using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Contacts;

// A person at a company, usually named in an ad. Kept per company rather than per listing,
// because one recruiter often covers several ads.
public class Contact
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    // LinkedIn or a profile page.
    public string? Url { get; set; }
    public string? Notes { get; set; }
}

public class ListingContact
{
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
}

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Company).WithMany(c => c.Contacts).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.CompanyId, c.NormalizedName }).IsUnique();
    }
}

public class ListingContactConfiguration : IEntityTypeConfiguration<ListingContact>
{
    public void Configure(EntityTypeBuilder<ListingContact> builder)
    {
        builder.HasKey(c => new { c.ListingId, c.ContactId });
        builder.HasOne(c => c.Listing).WithMany(l => l.Contacts).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Contact).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
