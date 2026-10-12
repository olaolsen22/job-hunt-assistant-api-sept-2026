using System.Net;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileCvParseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Parse = """
        mutation($text:String!){parseProfileCv(input:{text:$text}){
          result{status error candidate{schemaVersion basics{fullName} skills{name level} warnings{path}}}
          errors{__typename}
        }}
        """;

    [Theory]
    [InlineData("Plain text CV")]
    [InlineData("# Example Person\n## Experience\n- Built tools")]
    public async Task BuiltInUsesSharedPromptAndDoesNotSaveContent(string text)
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"schemaVersion":1,"contentLanguage":"ENGLISH","basics":{"fullName":"Unreviewed Name"},"skills":[{"name":"New Unreviewed Skill"}]}"""))
        { Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50, ReasoningTokenCount = 10 } };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await Counts(db);
        var profileBefore = (await client.GraphQLAsync("{profile{fullName}}" )).GetRawText();
        var result = (await client.GraphQLAsync(Parse, new { text })).GetProperty("parseProfileCv").GetProperty("result");
        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("candidate").GetProperty("skills")[0].GetProperty("level").ValueKind);
        Assert.Equal(ProfileCvPromptQueries.Instructions, factory.Llm.LastMessages[0].Text);
        Assert.Equal($"<cv>\n{text}\n</cv>", factory.Llm.LastMessages[1].Text);
        Assert.Equal(ChatResponseFormat.Json, factory.Llm.LastOptions!.ResponseFormat);
        Assert.Equal(before, await Counts(db));
        Assert.Equal(profileBefore, (await client.GraphQLAsync("{profile{fullName}}" )).GetRawText());
        var usage = await db.LlmUsages.IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.True(usage.Success); Assert.Equal(ProfileCvParser.Operation, usage.Operation);
        Assert.Equal(100, usage.InputTokens); Assert.Equal(60, usage.OutputTokens);
    }

    [Fact]
    public async Task EntireAcceptedInputReachesAi()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new(new ChatMessage(ChatRole.Assistant, ProfileCvCandidateTests.Empty));
        var text = new string('x', 99_990) + " CV END ";
        var result = await client.GraphQLAsync(Parse, new { text });
        Assert.Equal("SUCCEEDED", result.GetProperty("parseProfileCv").GetProperty("result").GetProperty("status").GetString());
        Assert.Contains(" CV END ", factory.Llm.LastMessages[1].Text);
        Assert.Equal(text.Length + 11, factory.Llm.LastMessages[1].Text.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100001)]
    public async Task InvalidTextDoesNotCallAiOrLogUsage(int length)
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => throw new Exception("Must not call");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.LlmUsages.IgnoreQueryFilters().CountAsync();
        var payload = (await client.GraphQLAsync(Parse, new { text = length == 0 ? "  " : new string('x', length) })).GetProperty("parseProfileCv");
        Assert.Equal("InvalidParseRequestError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(before, await db.LlmUsages.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("provider-error")]
    [InlineData("rate-limited")]
    [InlineData("timeout")]
    public async Task FailuresAreReadableAndLogOnlyMetadata(string error)
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => error switch
        {
            "invalid-json" => new(new ChatMessage(ChatRole.Assistant, "private invalid output")),
            "rate-limited" => throw new HttpRequestException("private provider response", null, HttpStatusCode.TooManyRequests),
            "timeout" => throw new TaskCanceledException("private timeout"),
            _ => throw new HttpRequestException("private CV provider response"),
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await Counts(db);
        var result = (await client.GraphQLAsync(Parse, new { text = "Private CV" })).GetProperty("parseProfileCv").GetProperty("result");
        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.DoesNotContain("private", result.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await Counts(db));
        var usage = await db.LlmUsages.IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.False(usage.Success); Assert.Equal(error, usage.ErrorCode);
    }

    private static async Task<(int Jobs, int Skills, int Locations)> Counts(AppDbContext db) =>
        (await db.ParseJobs.IgnoreQueryFilters().CountAsync(), await db.Skills.IgnoreQueryFilters().CountAsync(), await db.Locations.IgnoreQueryFilters().CountAsync());
}
