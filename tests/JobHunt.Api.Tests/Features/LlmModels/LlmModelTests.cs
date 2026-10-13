using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using JobHunt.Api.Tests.Features.Listings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobHunt.Api.Tests.Features.LlmModels;

public class LlmModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Default = ApiFactory.DefaultModel;
    private const string Strong = ApiFactory.StrongModel;

    private const string Models = "{ llmModels { models { id label } defaultModel } }";
    private const string SetDefault = """
        mutation ($model: String) {
          setDefaultLlmModel(input: { model: $model }) {
            llmModels { models { id } defaultModel }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string ParseListing = """
        mutation ($model: String) {
          parseListing(input: { text: "Vi søker en utvikler", mode: FULL, model: $model }) {
            parseJob { status }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string ParseUrl = """
        mutation ($model: String) {
          parseUrl(input: { url: "https://example.com/job", model: $model }) {
            parseJob { status }
            errors { __typename }
          }
        }
        """;
    private const string ParseListingFromUrl = """
        mutation ($model: String) {
          parseListingFromUrl(input: { listingId: 0, model: $model }) {
            parseJob { status }
            errors { __typename }
          }
        }
        """;
    private const string ParseCv = """
        mutation ($model: String) {
          parseProfileCv(input: { text: "Plain text CV", model: $model }) {
            result { status }
            errors { __typename }
          }
        }
        """;
    private const string Analyze = """
        mutation ($listingId: Int!, $model: String) {
          analyzeTailoredCvSection(input: { listingId: $listingId, section: SKILLS, model: $model }) {
            result { status }
            errors { __typename }
          }
        }
        """;
    private const string AnalyzeAll = """
        mutation ($model: String) {
          analyzeAllTailoredCv(input: { listingId: 0, model: $model }) {
            analysis { results { status } }
            errors { __typename }
          }
        }
        """;

    private static ChatResponse Answer(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    private static string[] Ids(JsonElement models) =>
        [.. models.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("id").GetString()!)];

    private async Task<(int Jobs, int Usages)> CountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ParseJobs.IgnoreQueryFilters().CountAsync(), await db.LlmUsages.IgnoreQueryFilters().CountAsync());
    }

    private async Task<LlmUsage> LatestUsageAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
    }

    private static string? FirstError(JsonElement payload) =>
        payload.GetProperty("errors").ValueKind == JsonValueKind.Array && payload.GetProperty("errors").GetArrayLength() > 0
            ? payload.GetProperty("errors")[0].GetProperty("__typename").GetString()
            : null;

    [Fact]
    public async Task Owner_GetsEveryConfiguredModel_AndGuestOnlyTheDefault()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });

        var ownerModels = (await owner.GraphQLAsync(Models)).GetProperty("llmModels");
        var guestModels = (await guest.GraphQLAsync(Models)).GetProperty("llmModels");

        Assert.Equal([Default, Strong], Ids(ownerModels));
        Assert.Equal(Default, ownerModels.GetProperty("defaultModel").GetString());
        Assert.Equal([Default], Ids(guestModels));
        Assert.Equal(Default, guestModels.GetProperty("defaultModel").GetString());
    }

    [Fact]
    public async Task LlmModels_RequiresSignIn()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/graphql", new { query = Models });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("errors", out _) || !response.IsSuccessStatusCode);
        Assert.False(body.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("llmModels", out var models) && models.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task SetDefault_ChangesOnlyTheOwner_AndNullClearsIt()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();

        var set = (await owner.GraphQLAsync(SetDefault, new { model = Strong })).GetProperty("setDefaultLlmModel");
        Assert.Null(FirstError(set));
        Assert.Equal(Strong, set.GetProperty("llmModels").GetProperty("defaultModel").GetString());
        Assert.Equal(Default, (await guest.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("defaultModel").GetString());

        var cleared = (await owner.GraphQLAsync(SetDefault, new { model = (string?)null })).GetProperty("setDefaultLlmModel");
        Assert.Equal(Default, cleared.GetProperty("llmModels").GetProperty("defaultModel").GetString());
    }

    [Theory]
    [InlineData(Strong)]
    [InlineData("gemini-ultra-unlisted")]
    public async Task Guest_CannotSetAModelOutsideItsList(string model)
    {
        var guest = await factory.GetGuestClientAsync();

        var payload = (await guest.GraphQLAsync(SetDefault, new { model })).GetProperty("setDefaultLlmModel");

        Assert.Equal("UnknownLlmModelError", FirstError(payload));
        Assert.Equal("That model isn't available.", payload.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal(Default, (await guest.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("defaultModel").GetString());
    }

    [Fact]
    public async Task StoredDefaultRemovedFromConfig_FallsBackToTheConfiguredDefault()
    {
        var owner = await factory.GetOwnerClientAsync();
        await owner.GraphQLAsync(SetDefault, new { model = Strong });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var setting = await db.UserLlmSettings.IgnoreQueryFilters().SingleAsync(s => s.DefaultModel == Strong);
            setting.DefaultModel = "model-dropped-from-config";
            await db.SaveChangesAsync();
        }

        var models = (await owner.GraphQLAsync(Models)).GetProperty("llmModels");

        Assert.Equal(Default, models.GetProperty("defaultModel").GetString());
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });
    }

    [Fact]
    public async Task ParseListing_UsesTheOverride_AndRecordsIt()
    {
        var owner = await factory.GetOwnerClientAsync();
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });
        factory.Llm.Respond = () => Answer("""{ "title": "Utvikler" }""");

        var payload = (await owner.GraphQLAsync(ParseListing, new { model = Strong })).GetProperty("parseListing");

        Assert.Equal("SUCCEEDED", payload.GetProperty("parseJob").GetProperty("status").GetString());
        Assert.Equal(Strong, factory.Llm.LastOptions!.ModelId);
        Assert.Equal(Strong, (await LatestUsageAsync()).Model);
        using var scope = factory.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ParseJobs
            .IgnoreQueryFilters().OrderByDescending(j => j.CreatedAt).FirstAsync();
        Assert.Equal(Strong, job.Model);
    }

    [Fact]
    public async Task ParseListing_WithoutOverride_UsesTheStoredDefault()
    {
        var owner = await factory.GetOwnerClientAsync();
        await owner.GraphQLAsync(SetDefault, new { model = Strong });
        factory.Llm.Respond = () => Answer("""{ "title": "Utvikler" }""");

        await owner.GraphQLAsync(ParseListing, new { model = (string?)null });

        Assert.Equal(Strong, factory.Llm.LastOptions!.ModelId);
        Assert.Equal(Strong, (await LatestUsageAsync()).Model);
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });
    }

    [Fact]
    public async Task ParseProfileCv_UsesTheOverride_AndDefaultOtherwise()
    {
        var owner = await factory.GetOwnerClientAsync();
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });
        factory.Llm.Respond = () => Answer("""{"schemaVersion":1,"contentLanguage":"ENGLISH","basics":{"fullName":"X"}}""");

        await owner.GraphQLAsync(ParseCv, new { model = Strong });
        Assert.Equal(Strong, factory.Llm.LastOptions!.ModelId);
        Assert.Equal(Strong, (await LatestUsageAsync()).Model);

        await owner.GraphQLAsync(ParseCv, new { model = (string?)null });
        Assert.Equal(Default, factory.Llm.LastOptions!.ModelId);
        Assert.Equal(Default, (await LatestUsageAsync()).Model);
    }

    [Fact]
    public async Task AnalyzeTailoredCvSection_UsesTheOverride()
    {
        var owner = await factory.GetOwnerClientAsync();
        var listingId = await ListingTests.ListingWithStatusAsync(owner, ListingTests.UniqueUrl(), "SAVED");
        factory.Llm.Respond = () => Answer("""{"selectedSkillIds":[]}""");

        await owner.GraphQLAsync(Analyze, new { listingId, model = Strong });

        Assert.Equal(Strong, factory.Llm.LastOptions!.ModelId);
        Assert.Equal(Strong, (await LatestUsageAsync()).Model);
    }

    public static TheoryData<string> AiMutations => new() { "parseListing", "parseUrl", "parseListingFromUrl", "parseProfileCv", "analyzeTailoredCvSection", "analyzeAllTailoredCv" };

    private static (string Query, Func<string, object> Variables) MutationFor(string name) => name switch
    {
        "parseListing" => (ParseListing, m => new { model = m }),
        "parseUrl" => (ParseUrl, m => new { model = m }),
        "parseListingFromUrl" => (ParseListingFromUrl, m => new { model = m }),
        "parseProfileCv" => (ParseCv, m => new { model = m }),
        "analyzeTailoredCvSection" => (Analyze, m => new { listingId = 0, model = m }),
        _ => (AnalyzeAll, m => new { model = m }),
    };

    [Theory]
    [MemberData(nameof(AiMutations))]
    public async Task Guest_RequestingAnotherModel_IsRejectedBeforeAnyCall(string mutation)
    {
        var guest = await factory.GetGuestClientAsync();
        await AssertRejectedAsync(guest, mutation, Strong);
    }

    [Theory]
    [MemberData(nameof(AiMutations))]
    public async Task Owner_UnknownOrMiscasedModel_IsRejectedBeforeAnyCall(string mutation)
    {
        var owner = await factory.GetOwnerClientAsync();
        await AssertRejectedAsync(owner, mutation, "gemini-ultra-unlisted");
        await AssertRejectedAsync(owner, mutation, Default.ToUpperInvariant());
        await AssertRejectedAsync(owner, mutation, new string('x', 10_000));
    }

    private async Task AssertRejectedAsync(HttpClient client, string mutation, string model)
    {
        var (query, variables) = MutationFor(mutation);
        var calls = 0;
        factory.Llm.Respond = () => { calls++; return Answer("{}"); };
        factory.Pages.Respond = () => throw new InvalidOperationException("No page fetch expected.");
        var before = await CountsAsync();

        var payload = (await client.GraphQLAsync(query, variables(model))).GetProperty(mutation);

        Assert.Equal("UnknownLlmModelError", FirstError(payload));
        Assert.Equal(0, calls);
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public void InvalidConfig_FailsValidation()
    {
        Assert.Throws<OptionsValidationException>(() =>
        {
            using var bad = factory.WithWebHostBuilder(b => b.UseSetting("Llm:Gemini:DefaultModel", "not-in-the-list"));
            bad.Services.GetRequiredService<IOptions<GeminiOptions>>().Value.ToString();
        });
    }
}
