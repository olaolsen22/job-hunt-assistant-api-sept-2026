using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Data;

public class QueryFilterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // User isolation rests on these filters, and adding one to AppDbContext is a manual step for each new entity.
    [Fact]
    public void EveryEntity_HasAUserFilter()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unfiltered = db.Model.GetEntityTypes()
            .Where(e => e.ClrType.Namespace?.StartsWith("JobHunt.Api", StringComparison.Ordinal) == true
                && e.ClrType != typeof(AppUser) && !e.IsOwned() && !e.GetDeclaredQueryFilters().Any())
            .Select(e => e.ClrType.Name);

        Assert.Empty(unfiltered);
    }
}
