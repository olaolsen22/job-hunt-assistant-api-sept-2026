using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Samples;

// Temporary scaffold check that the DB round-trip works. Remove in M1.
public class Sample
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.HasData(
            new Sample { Id = 1, Name = "Acme Corp — Frontend Engineer" },
            new Sample { Id = 2, Name = "Globex — Fullstack Developer" },
            new Sample { Id = 3, Name = "Initech — .NET Developer" });
    }
}
