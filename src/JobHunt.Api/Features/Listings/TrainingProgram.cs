using JobHunt.Api.Features.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Listings;

// An academy or bootcamp that leads to a job. The listing's Company is the provider; the employer is often unnamed.
public class TrainingProgram
{
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public int? EndCompanyId { get; set; }
    public Company? EndCompany { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    // Null = the ad doesn't say. Many programs are free but unpaid, sometimes with a loan option.
    public bool? IsPaid { get; set; }
    public JobOfferType JobOffer { get; set; }
}

// What the ad promises after the program: a job no matter what, a job if you pass, or nothing concrete.
public enum JobOfferType { Guaranteed, OnPass, None, Unspecified }

public class TrainingProgramConfiguration : IEntityTypeConfiguration<TrainingProgram>
{
    public void Configure(EntityTypeBuilder<TrainingProgram> builder)
    {
        builder.HasKey(t => t.ListingId);
        builder.HasOne(t => t.Listing).WithOne(l => l.TrainingProgram)
            .HasForeignKey<TrainingProgram>(t => t.ListingId).OnDelete(DeleteBehavior.Cascade);
        // NoAction for the same reason as Listing.Company: lets a user delete cascade through in SQLite.
        builder.HasOne(t => t.EndCompany).WithMany().OnDelete(DeleteBehavior.NoAction);
    }
}
