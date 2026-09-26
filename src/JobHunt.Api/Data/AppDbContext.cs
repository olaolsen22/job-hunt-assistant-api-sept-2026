using JobHunt.Api.Features.Samples;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Sample> Samples => Set<Sample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
