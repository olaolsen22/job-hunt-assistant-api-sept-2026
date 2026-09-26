using System.Net.Http.Json;
using JobHunt.Api.Features.Samples;

namespace JobHunt.Api.Tests.Features.Samples;

public class SampleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetSamples_ReturnsSeededRows()
    {
        var client = factory.CreateClient();

        var samples = await client.GetFromJsonAsync<List<SampleResponse>>("/api/samples");

        Assert.NotNull(samples);
        Assert.Equal(3, samples.Count);
        Assert.Contains(samples, s => s.Name == "Acme Corp — Frontend Engineer");
    }
}
