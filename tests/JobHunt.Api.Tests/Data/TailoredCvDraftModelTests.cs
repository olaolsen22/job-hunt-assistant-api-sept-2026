using JobHunt.Api.Data;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Data;

public class TailoredCvDraftModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Draft_HasOwnerAndListingForeignKeys_AndIsUniquePerListing()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.Model.FindEntityType(typeof(TailoredCvDraft))!;

        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual([nameof(TailoredCvDraft.ListingId)]));
        Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(TailoredCvDraft.UserId)]));
        Assert.Contains(entity.GetForeignKeys(), fk => fk.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(TailoredCvDraft.UserId)]) && fk.PrincipalEntityType.ClrType.Name == "AppUser");
        Assert.Contains(entity.GetForeignKeys(), fk => fk.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(TailoredCvDraft.ListingId)]) && fk.PrincipalEntityType.ClrType.Name == "Listing");
        Assert.NotEmpty(entity.GetDeclaredQueryFilters());
    }

    [Fact]
    public async Task Database_RejectsTwoDraftsForOneListing()
    {
        var client = await factory.GetOwnerClientAsync();
        var data = await client.GraphQLAsync("""
            mutation {
              createListing(input: { listing: {
                title: "Draft model test", companyName: "Draft model test company",
                jobRoleName: "Developer", url: "https://draft-model-test.example/job",
                postedOn: "2026-10-01", deadlineType: UNSPECIFIED,
                skills: [], languages: [], locations: []
              } }) { listing { id } }
            }
            """);
        var listingId = data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Listings.IgnoreQueryFilters().Where(l => l.Id == listingId)
            .Select(l => l.UserId).SingleAsync();
        db.TailoredCvDrafts.Add(new() { UserId = userId, ListingId = listingId, ContentJson = "{}" });
        await db.SaveChangesAsync();
        db.TailoredCvDrafts.Add(new() { UserId = userId, ListingId = listingId, ContentJson = "{}" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
