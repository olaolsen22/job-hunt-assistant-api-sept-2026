using System.Text.Json;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileCvCandidateTests
{
    public const string Empty = """{"schemaVersion":1,"contentLanguage":"ENGLISH"}""";

    [Fact]
    public void IncompleteValuesRemainAndHaveReviewWarnings()
    {
        var candidate = ProfileCvDecoder.Decode("""
            {"schemaVersion":1,"contentLanguage":"ENGLISH",
             "experiences":[{"employer":"Acme","title":"Engineer","startMonth":"2020","bullets":["Built services"]}],
             "skills":[{"name":"Vue","level":null}], "languages":[{"name":"Norwegian","written":null,"spoken":"GOOD"}]}
            """);
        Assert.Equal("2020", candidate.Experiences[0].StartMonth);
        Assert.Null(candidate.Skills[0].Level);
        Assert.Contains(candidate.Warnings, w => w.Path == "experiences[0].startMonth");
        Assert.Contains(candidate.Warnings, w => w.Path == "skills[0].level");
        Assert.Contains(candidate.Warnings, w => w.Path == "languages[0].written");
        Assert.DoesNotContain(candidate.Warnings, w => w.Path == "languages[0].spoken");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":2,\"contentLanguage\":\"ENGLISH\"}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":\"NORWEGIAN\"}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":0}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":\"ENGLISH\",\"dateOfBirth\":\"1990-01-01\"}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"contentLanguage\":\"ENGLISH\"}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":\"ENGLISH\",\"projects\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":\"ENGLISH\",\"skills\":null}")]
    [InlineData("{\"schemaVersion\":1,\"contentLanguage\":\"ENGLISH\",\"skills\":[{\"name\":7}]}")]
    public void MalformedCandidatesAreRejected(string json) => Assert.Throws<InvalidProfileImportException>(() => ProfileCvDecoder.Decode(json));

    [Fact]
    public void LimitsAreEnforced()
    {
        Assert.Throws<InvalidProfileImportException>(() => ProfileCvDecoder.Decode(new string(' ', 200_001)));
        var tooMany = JsonSerializer.Serialize(new { schemaVersion = 1, contentLanguage = "ENGLISH", projects = Enumerable.Repeat(new { name = "p" }, 101) });
        Assert.Throws<InvalidProfileImportException>(() => ProfileCvDecoder.Decode(tooMany));
        var bullets = JsonSerializer.Serialize(new { schemaVersion = 1, contentLanguage = "ENGLISH", projects = new[] { new { name = "p", bullets = Enumerable.Repeat("b", 21) } } });
        Assert.Throws<InvalidProfileImportException>(() => ProfileCvDecoder.Decode(bullets));
    }

    [Fact]
    public void FieldRuleWarningsAreReturnedWithoutInventingCorrections()
    {
        var candidate = ProfileCvDecoder.Decode("""
            {"schemaVersion":1,"contentLanguage":"ENGLISH","basics":{"email":"bad","location":{"city":"Unknown","countryCode":"XX"}},
             "projects":[{"name":"","url":"ftp://example.com","bullets":[""]}],"skills":[{"name":"Vue","level":6}]}
            """);
        foreach (var path in new[] { "basics.email", "basics.location.countryCode", "projects[0].name", "projects[0].url", "projects[0].bullets[0]", "skills[0].level" })
            Assert.Contains(candidate.Warnings, w => w.Path == path);
        Assert.Equal("bad", candidate.Basics!.Email);
    }

    [Fact]
    public void DocumentationCandidateDecodes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "JobHunt.slnx"))) root = root.Parent!;
        var docs = File.ReadAllText(Path.Combine(root.FullName, "docs/profile-cv-import.md"));
        var start = docs.IndexOf("```json\n", StringComparison.Ordinal) + 8;
        var end = docs.IndexOf("```", start, StringComparison.Ordinal);
        Assert.Equal("Example Person", ProfileCvDecoder.Decode(docs[start..end]).Basics!.FullName);
    }
}

public class ProfileCvPreviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task PromptAndPreviewDoNotUseAiOrSaveProfile()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => throw new Exception("AI must not be called");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var skills = await db.Skills.IgnoreQueryFilters().CountAsync();
        var locations = await db.Locations.IgnoreQueryFilters().CountAsync();
        var before = await client.GraphQLAsync("{profile{fullName projects{id}} llmUsageToday{requests}}");
        var prompt = (await client.GraphQLAsync("{profileCvParsePrompt{version instructions}}" )).GetProperty("profileCvParsePrompt");
        Assert.Equal(2, prompt.GetProperty("version").GetInt32());
        Assert.Equal(ProfileCvPromptQueries.Instructions, prompt.GetProperty("instructions").GetString());
        Assert.Contains("Do not invent dates or proficiency levels", prompt.GetProperty("instructions").GetString());
        var result = await client.GraphQLAsync("""
            mutation($json:String!){previewProfileCvImport(input:{json:$json}){candidate{schemaVersion contentLanguage projects{name} warnings{path message}} errors{__typename}}}
            """, new { json = """{"schemaVersion":1,"contentLanguage":"ENGLISH","projects":[{"name":"Imported project"}]}""" });
        Assert.Equal("Imported project", result.GetProperty("previewProfileCvImport").GetProperty("candidate").GetProperty("projects")[0].GetProperty("name").GetString());
        var after = await client.GraphQLAsync("{profile{fullName projects{id}} llmUsageToday{requests}}");
        Assert.Equal(before.GetRawText(), after.GetRawText());
        Assert.Equal(skills, await db.Skills.IgnoreQueryFilters().CountAsync());
        Assert.Equal(locations, await db.Locations.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task PreviewErrorsAreTyped()
    {
        var client = await factory.GetOwnerClientAsync();
        var result = await client.GraphQLAsync("""
            mutation{previewProfileCvImport(input:{json:"[]"}){candidate{schemaVersion} errors{__typename ... on InvalidProfileImportError{message}}}}
            """);
        Assert.Equal("InvalidProfileImportError", result.GetProperty("previewProfileCvImport").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }
}
