using System.Net;

namespace JobHunt.Api.Tests;

// Stands in for the web: a test sets what a listing page answers and checks whether it was fetched at all.
public class FakePageHandler : HttpMessageHandler
{
    public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(Respond());
    }
}
