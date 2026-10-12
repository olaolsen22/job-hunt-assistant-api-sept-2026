using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.JobRoles;

public class JobRoleCategoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string CreateCategory = """
        mutation ($name: String!) { createJobRoleCategory(input:{name:$name}) {
          jobRoleCategory { id name } errors { __typename ... on Error { message } }
        } }
        """;
    internal const string AssignCategory = """
        mutation ($jobRoleId:Int!, $categoryId:Int) { setJobRoleCategory(input:{jobRoleId:$jobRoleId,categoryId:$categoryId}) {
          jobRole { id categoryId category { id name } } errors { __typename }
        } }
        """;
    private const string RenameCategory = """
        mutation ($id:Int!, $name:String!) { updateJobRoleCategory(input:{id:$id,name:$name}) {
          jobRoleCategory { id name } errors { __typename ... on DuplicateJobRoleCategoryError { existingCategoryId } }
        } }
        """;
    internal const string Save = """
        mutation ($listing:ListingInput!) { createListing(input:{listing:$listing}) {
          listing { id jobRole { id categoryId category { id name } } } errors { __typename }
        } }
        """;
    private const string Update = """
        mutation ($id:Int!, $listing:ListingInput!) { updateListing(input:{id:$id,listing:$listing}) {
          listing { id title jobRole { id categoryId category { id name } } } errors { __typename }
        } }
        """;

    internal static Dictionary<string, object?> Input(string role, string? category = null, string? url = null) => new()
    {
        ["title"] = "Backend Developer", ["companyName"] = ListingTests.Unique("Bank"), ["jobRoleName"] = role,
        ["url"] = url ?? ListingTests.UniqueUrl(), ["postedOn"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["skills"] = Array.Empty<object>(), ["languages"] = Array.Empty<object>(), ["locations"] = Array.Empty<object>(),
        ["jobRoleCategoryName"] = category,
    };
    internal static async Task<int> CategoryAsync(HttpClient client, string name) =>
        (await client.GraphQLAsync(CreateCategory, new { name })).GetProperty("createJobRoleCategory")
            .GetProperty("jobRoleCategory").GetProperty("id").GetInt32();
    private static string Error(JsonElement payload) => payload.GetProperty("errors")[0].GetProperty("__typename").GetString()!;
    private static int RoleId(JsonElement listing) => listing.GetProperty("jobRole").GetProperty("id").GetInt32();
    private async Task<(int Categories, int Roles, int Listings, int Companies)> CountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.JobRoleCategories.IgnoreQueryFilters().CountAsync(), await db.JobRoles.IgnoreQueryFilters().CountAsync(),
            await db.Listings.IgnoreQueryFilters().CountAsync(), await db.Companies.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Categories_CreateReuseRenameValidate_AndDiscoverAllPages()
    {
        var owner = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Økonomi");
        var id = await CategoryAsync(owner, "  " + name + "  ");
        Assert.Equal(id, await CategoryAsync(owner, name.ToLowerInvariant()));
        var rename = (await owner.GraphQLAsync(RenameCategory, new { id, name = "  " + name + " renamed  " }))
            .GetProperty("updateJobRoleCategory").GetProperty("jobRoleCategory");
        Assert.Equal(name + " renamed", rename.GetProperty("name").GetString());
        var it = await CategoryAsync(owner, "IT");
        var collision = (await owner.GraphQLAsync(RenameCategory, new { id, name = " it " })).GetProperty("updateJobRoleCategory");
        Assert.Equal("DuplicateJobRoleCategoryError", Error(collision));
        Assert.Equal(it, collision.GetProperty("errors")[0].GetProperty("existingCategoryId").GetInt32());
        foreach (var invalid in new[] { "  ", new string('x', 101) })
        {
            Assert.Equal("InvalidJobRoleCategoryError", Error((await owner.GraphQLAsync(CreateCategory, new { name = invalid })).GetProperty("createJobRoleCategory")));
            Assert.Equal("InvalidJobRoleCategoryError", Error((await owner.GraphQLAsync(RenameCategory, new { id, name = invalid })).GetProperty("updateJobRoleCategory")));
        }
        var search = (await owner.GraphQLAsync("query($search:String!){jobRoleCategories(search:$search){nodes{id name}}}", new { search = name.ToLowerInvariant() }))
            .GetProperty("jobRoleCategories").GetProperty("nodes");
        Assert.Equal(id, Assert.Single(search.EnumerateArray()).GetProperty("id").GetInt32());
        var found = new List<string>();
        string? after = null;
        do
        {
            var page = (await owner.GraphQLAsync("""
                query($after:String){ jobRoleCategories(first:2,after:$after,order:[{name:ASC}]) {
                  nodes{name} pageInfo{hasNextPage endCursor}
                } }
                """, new { after })).GetProperty("jobRoleCategories");
            found.AddRange(page.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()!));
            after = page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean()
                ? page.GetProperty("pageInfo").GetProperty("endCursor").GetString() : null;
        } while (after is not null);
        Assert.Contains(name + " renamed", found);
        Assert.Equal(found.Order(StringComparer.Ordinal).ToArray(), found.ToArray());
        var filtered = await owner.GraphQLAsync("query($id:Int!){jobRoleCategories(where:{id:{eq:$id}}){nodes{id}}}", new { id });
        Assert.Equal(id, Assert.Single(filtered.GetProperty("jobRoleCategories").GetProperty("nodes").EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Assignments_AffectAllListings_Clear_AndRejectForeignIds()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var role = ListingTests.Unique("Role");
        var first = (await owner.GraphQLAsync(Save, new { listing = Input(role) })).GetProperty("createListing").GetProperty("listing");
        var second = (await owner.GraphQLAsync(Save, new { listing = Input(role) })).GetProperty("createListing").GetProperty("listing");
        var roleId = RoleId(first);
        var categoryId = await CategoryAsync(owner, ListingTests.Unique("Healthcare"));
        await owner.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId });
        foreach (var listing in new[] { first, second })
        {
            var current = (await owner.GraphQLAsync("query($id:Int!){listingById(id:$id){jobRole{categoryId category{id}}}}", new { id = listing.GetProperty("id").GetInt32() })).GetProperty("listingById");
            Assert.Equal(categoryId, current.GetProperty("jobRole").GetProperty("categoryId").GetInt32());
            Assert.Equal(categoryId, current.GetProperty("jobRole").GetProperty("category").GetProperty("id").GetInt32());
        }
        var cleared = (await owner.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId = (int?)null })).GetProperty("setJobRoleCategory");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("jobRole").GetProperty("categoryId").ValueKind);
        var foreign = await CategoryAsync(guest, ListingTests.Unique("Guest category"));
        foreach (var invalid in new[] { foreign, int.MaxValue })
            Assert.Equal("JobRoleCategoryNotFoundError", Error((await owner.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId = invalid })).GetProperty("setJobRoleCategory")));
        Assert.Equal("JobRoleNotFoundError", Error((await guest.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId = foreign })).GetProperty("setJobRoleCategory")));
        Assert.Equal("JobRoleNotFoundError", Error((await owner.GraphQLAsync(AssignCategory, new { jobRoleId = int.MaxValue, categoryId })).GetProperty("setJobRoleCategory")));
        foreach (var invalid in new[] { foreign, int.MaxValue })
            Assert.Equal("JobRoleCategoryNotFoundError", Error((await owner.GraphQLAsync(RenameCategory, new { id = invalid, name = "X" })).GetProperty("updateJobRoleCategory")));
        var privateCategories = await guest.GraphQLAsync("query($id:Int!){jobRoleCategories(where:{id:{eq:$id}}){nodes{id}}}", new { id = categoryId });
        Assert.Empty(privateCategories.GetProperty("jobRoleCategories").GetProperty("nodes").EnumerateArray());
        var privateRoles = await guest.GraphQLAsync("query($id:Int!){jobRoles(where:{id:{eq:$id}}){nodes{id categoryId}}}", new { id = roleId });
        Assert.Empty(privateRoles.GetProperty("jobRoles").GetProperty("nodes").EnumerateArray());
    }

    [Fact]
    public async Task SaveSuggestions_AssignUnknownRoles_PreserveCorrections_AndAllowRecategorizationAfterClear()
    {
        var owner = await factory.GetOwnerClientAsync();
        var role = ListingTests.Unique("Custom developer");
        var input = Input(role, " it ");
        var first = (await owner.GraphQLAsync(Save, new { listing = input })).GetProperty("createListing").GetProperty("listing");
        Assert.Equal("IT", first.GetProperty("jobRole").GetProperty("category").GetProperty("name").GetString());
        var roleId = RoleId(first);
        var healthcare = await CategoryAsync(owner, "Healthcare");
        await owner.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId = healthcare });
        var updated = (await owner.GraphQLAsync(Update, new { id = first.GetProperty("id").GetInt32(), listing = input })).GetProperty("updateListing").GetProperty("listing");
        Assert.Equal(healthcare, updated.GetProperty("jobRole").GetProperty("categoryId").GetInt32());
        foreach (var suggestion in new string?[] { null, " " })
        {
            var saved = (await owner.GraphQLAsync(Save, new { listing = Input(role, suggestion) })).GetProperty("createListing").GetProperty("listing");
            Assert.Equal(healthcare, saved.GetProperty("jobRole").GetProperty("categoryId").GetInt32());
        }
        var legacy = Input(role); legacy.Remove("jobRoleCategoryName");
        Assert.Equal(healthcare, (await owner.GraphQLAsync(Save, new { listing = legacy })).GetProperty("createListing").GetProperty("listing").GetProperty("jobRole").GetProperty("categoryId").GetInt32());
        await owner.GraphQLAsync(AssignCategory, new { jobRoleId = roleId, categoryId = (int?)null });
        var recategorized = (await owner.GraphQLAsync(Save, new { listing = Input(role, "IT") })).GetProperty("createListing").GetProperty("listing");
        Assert.Equal("IT", recategorized.GetProperty("jobRole").GetProperty("category").GetProperty("name").GetString());
        var old = Input(ListingTests.Unique("Unknown"));
        var oldListing = (await owner.GraphQLAsync(Save, new { listing = old })).GetProperty("createListing").GetProperty("listing");
        Assert.Equal(JsonValueKind.Null, oldListing.GetProperty("jobRole").GetProperty("categoryId").ValueKind);
        old["jobRoleCategoryName"] = "Healthcare";
        var categorized = (await owner.GraphQLAsync(Update, new { id = oldListing.GetProperty("id").GetInt32(), listing = old })).GetProperty("updateListing").GetProperty("listing");
        Assert.Equal(healthcare, categorized.GetProperty("jobRole").GetProperty("categoryId").GetInt32());
    }

    [Fact]
    public async Task InvalidSuggestions_CreateAndUpdate_PersistNothing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var guestName = ListingTests.Unique("Foreign");
        await CategoryAsync(guest, guestName);
        var valid = Input(ListingTests.Unique("Existing"), "IT");
        var listingId = (await owner.GraphQLAsync(Save, new { listing = valid })).GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
        foreach (var name in new[] { guestName, ListingTests.Unique("Missing"), new string('x', 101) })
        {
            var counts = await CountsAsync();
            Assert.Equal("InvalidListingError", Error((await owner.GraphQLAsync(Save, new { listing = Input(ListingTests.Unique("New role"), name) })).GetProperty("createListing")));
            valid["jobRoleCategoryName"] = name;
            valid["title"] = "Must not persist";
            Assert.Equal("InvalidListingError", Error((await owner.GraphQLAsync(Update, new { id = listingId, listing = valid })).GetProperty("updateListing")));
            Assert.Equal(counts, await CountsAsync());
            var current = (await owner.GraphQLAsync("query($id:Int!){listingById(id:$id){title jobRole{category{name}}}}", new { id = listingId })).GetProperty("listingById");
            Assert.Equal("Backend Developer", current.GetProperty("title").GetString());
            Assert.Equal("IT", current.GetProperty("jobRole").GetProperty("category").GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task ITFilter_UsesRoleCategory_RatherThanCompanyIndustry()
    {
        var owner = await factory.GetOwnerClientAsync();
        var it = await CategoryAsync(owner, "IT");
        var inputs = new[]
        {
            Input(ListingTests.Unique("Developer"), "IT"),
            Input(ListingTests.Unique("Recruiter"), "Business & Administration"),
        };
        var ids = new List<int>();
        for (var i = 0; i < inputs.Length; i++)
        {
            var companyName = inputs[i]["companyName"];
            await owner.GraphQLAsync("""
                mutation($name:String!, $industry:String!){createCompanies(input:{companies:[{name:$name,industryName:$industry,locations:[]}]}){results{company{id}}}}
                """, new { name = companyName, industry = i == 0 ? "Banking" : "IT" });
            var listing = (await owner.GraphQLAsync(Save, new { listing = inputs[i] })).GetProperty("createListing").GetProperty("listing");
            ids.Add(listing.GetProperty("id").GetInt32());
        }
        var rows = (await owner.GraphQLAsync("""
            query($it:Int!, $ids:[Int]){listings(where:{id:{in:$ids},jobRole:{categoryId:{eq:$it}}}){
              items{id company{industry{name}} jobRole{category{name}}} totalCount
            }}
            """, new { it, ids })).GetProperty("listings");
        Assert.Equal(1, rows.GetProperty("totalCount").GetInt32());
        Assert.Equal(ids[0], rows.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal("Banking", rows.GetProperty("items")[0].GetProperty("company").GetProperty("industry").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ListingFilters_CategoryPagingCounts_Null_In_AndCombinedFilters()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var category = await CategoryAsync(owner, ListingTests.Unique("Technical"));
        var other = await CategoryAsync(owner, ListingTests.Unique("Recruitment"));
        var matching = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var input = Input(ListingTests.Unique("Developer"));
            input["companyName"] = ListingTests.Unique("Bank");
            var row = (await owner.GraphQLAsync(Save, new { listing = input })).GetProperty("createListing").GetProperty("listing");
            matching.Add(row.GetProperty("id").GetInt32());
            await owner.GraphQLAsync(AssignCategory, new { jobRoleId = RoleId(row), categoryId = category });
        }
        var recruiter = (await owner.GraphQLAsync(Save, new { listing = Input(ListingTests.Unique("Recruiter")) })).GetProperty("createListing").GetProperty("listing");
        await owner.GraphQLAsync(AssignCategory, new { jobRoleId = RoleId(recruiter), categoryId = other });
        var unknown = (await owner.GraphQLAsync(Save, new { listing = Input(ListingTests.Unique("Uncategorized")) })).GetProperty("createListing").GetProperty("listing");
        var page = (await owner.GraphQLAsync("""
            query($category:Int!){listings(take:2,order:[{id:ASC}],where:{jobRole:{categoryId:{eq:$category}}}){
              items{id jobRole{categoryId}} totalCount pageInfo{hasNextPage}
            }}
            """, new { category })).GetProperty("listings");
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(matching.Take(2), page.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetInt32()));
        Assert.True(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        var selected = (await owner.GraphQLAsync("query($ids:[Int]){listings(where:{jobRole:{categoryId:{in:$ids}}}){totalCount}}", new { ids = new[] { category, other } })).GetProperty("listings");
        Assert.Equal(4, selected.GetProperty("totalCount").GetInt32());
        var uncategorized = (await owner.GraphQLAsync("{listings(where:{jobRole:{categoryId:{eq:null}}}){items{id jobRole{categoryId}}}}"))
            .GetProperty("listings").GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(uncategorized, n => n.GetProperty("id").GetInt32() == unknown.GetProperty("id").GetInt32());
        Assert.All(uncategorized, n => Assert.Equal(JsonValueKind.Null, n.GetProperty("jobRole").GetProperty("categoryId").ValueKind));
        var privateResults = (await guest.GraphQLAsync("query($category:Int!){listings(where:{jobRole:{categoryId:{eq:$category}}}){totalCount}}", new { category })).GetProperty("listings");
        Assert.Equal(0, privateResults.GetProperty("totalCount").GetInt32());
        await owner.GraphQLAsync("mutation($id:Int!){applyToListing(input:{listingId:$id}){listing{id}}}", new { id = matching[0] });
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        const string combined = """
            query($category:Int!, $today:LocalDate!){listings(search:"backend",archived:false,dueWithinDays:30,
              where:{jobRole:{categoryId:{eq:$category}},status:{eq:APPLIED},postedOn:{gte:$today,lte:$today}},order:[{id:ASC}]){items{id} totalCount}}
            """;
        var filtered = (await owner.GraphQLAsync(combined, new { category, today })).GetProperty("listings");
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(matching[0], filtered.GetProperty("items")[0].GetProperty("id").GetInt32());
        await owner.GraphQLAsync("mutation($id:Int!){setListingArchived(input:{id:$id,isArchived:true}){listing{id}}}", new { id = matching[0] });
        Assert.Equal(0, (await owner.GraphQLAsync(combined, new { category, today })).GetProperty("listings").GetProperty("totalCount").GetInt32());
    }
}
