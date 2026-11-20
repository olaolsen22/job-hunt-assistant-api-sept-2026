namespace JobHunt.Api.Tests.Features;

// Queries have no typed errors, so these messages reach the UI only as top-level GraphQL errors.
public class PromptQueryErrorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("{ tailoredCvExternalPrompt(listingId: 999999) { version } }", "Listing 999999 was not found.")]
    [InlineData("{ coverLetterExternalPrompt(listingId: 999999) { version } }", "Listing 999999 was not found.")]
    [InlineData("{ applicationAnswerExternalPrompt(listingId: 999999) { version } }", "Listing 999999 was not found.")]
    [InlineData("{ profileTranslationPrompt(language: ENGLISH) { version } }", "A translation can't be in the language the profile is written in.")]
    public async Task PromptQuery_ReportsTheRealReason(string query, string message)
    {
        var client = await factory.GetOwnerClientAsync();

        Assert.Equal(message, await client.GraphQLErrorAsync(query));
    }
}
