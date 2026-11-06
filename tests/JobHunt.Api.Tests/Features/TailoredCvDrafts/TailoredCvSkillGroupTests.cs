using System.Text.Json;
using System.Net.Http.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvSkillGroupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Suggest = """
        mutation ($skillNames: [String!]!, $language: Language!) {
          suggestTailoredCvSkillGroups(input: { skillNames: $skillNames, language: $language }) {
            result { status error groups { name group } }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task Suggest_KeepsSentSkillsInOrder_MergesLabelSpelling_AndSavesOnlyUsage()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {"groups":[{"name":"git","group":"Tools"},{"name":"Vue","group":"Frontend"},
                       {"name":"TypeScript","group":"frontend"},{"name":"Invented","group":"Backend"},
                       {"name":"Vue","group":"Backend"},{"name":"React","group":"  "}]}
            """));

        var result = (await client.GraphQLAsync(Suggest, new
        {
            skillNames = new[] { " TypeScript ", "React", "Vue", "Git" }, language = "ENGLISH",
        })).GetProperty("suggestTailoredCvSkillGroups").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        // Draft order; invented skill and blank label dropped; second label for Vue ignored; "frontend" → "Frontend".
        Assert.Equal(["TypeScript:Frontend", "Vue:Frontend", "Git:Tools"], result.GetProperty("groups").EnumerateArray()
            .Select(g => $"{g.GetProperty("name").GetString()}:{g.GetProperty("group").GetString()}"));
        Assert.Equal(TailoredCvSkillGroupSuggester.Instructions(Language.English, external: false), factory.Llm.LastMessages[0].Text);
        Assert.DoesNotContain("```json", factory.Llm.LastMessages[0].Text);
        Assert.Contains("TypeScript\nReact\nVue\nGit", factory.Llm.LastMessages[1].Text);
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(TailoredCvSkillGroupSuggester.Operation, usage.Operation);
    }

    private const string Prompt = """
        query ($skillNames: [String!]!, $language: Language!) {
          tailoredCvSkillGroupPrompt(skillNames: $skillNames, language: $language) { version instructions }
        }
        """;

    [Fact]
    public async Task Prompt_IncludesSkillsInOrder_CodeBlockRule_AndLanguage()
    {
        var client = await factory.GetOwnerClientAsync();

        var prompt = (await client.GraphQLAsync(Prompt, new { skillNames = new[] { " Vue ", "C#" }, language = "NORWEGIAN" }))
            .GetProperty("tailoredCvSkillGroupPrompt");

        var instructions = prompt.GetProperty("instructions").GetString()!;
        Assert.Equal(TailoredCvSkillGroupPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        Assert.Contains("```json", instructions);
        Assert.Contains("Norwegian bokmål", instructions);
        Assert.EndsWith("<skills>\nVue\nC#\n</skills>", instructions);
    }

    [Fact]
    public async Task Prompt_RejectsEmptySkillList()
    {
        var client = await factory.GetOwnerClientAsync();

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = Prompt, variables = new { skillNames = Array.Empty<string>(), language = "ENGLISH" },
        });

        Assert.Contains("Send 1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Suggest_UnreadableAnswer_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json"));

        var result = (await client.GraphQLAsync(Suggest, new { skillNames = new[] { "Vue" }, language = "NORWEGIAN" }))
            .GetProperty("suggestTailoredCvSkillGroups").GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("groups").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TailoredCvSkillGroupSuggester.MaxSkills + 1)]
    public async Task Suggest_RejectsSkillCountOutOfRange(int count)
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(Suggest, new
        {
            skillNames = Enumerable.Range(0, count).Select(i => $"Skill {i}").ToArray(), language = "ENGLISH",
        })).GetProperty("suggestTailoredCvSkillGroups");

        Assert.Equal("InvalidTailoredCvSkillGroupsError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Suggest_RejectsBlankName()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(Suggest, new { skillNames = new[] { "Vue", " " }, language = "ENGLISH" }))
            .GetProperty("suggestTailoredCvSkillGroups");

        Assert.Equal("InvalidTailoredCvSkillGroupsError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }
}
