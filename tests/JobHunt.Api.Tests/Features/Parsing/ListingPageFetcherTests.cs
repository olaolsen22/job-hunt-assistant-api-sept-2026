using System.Net;
using System.Net.Sockets;
using JobHunt.Api.Features.Parsing;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunt.Api.Tests.Features.Parsing;

public class ListingPageFetcherTests
{
    private static readonly Uri Url = new("https://jobs.example/ad/1");

    // Shaped like finn.no: JobPosting nested under a wrapper key, HTML entities in the description,
    // and other listings in the page body after the ad.
    private const string PageWithJobPosting = """
        <html><head>
        <script type="application/ld+json">{"@context":"https://schema.org"}</script>
        <script type="application/ld+json">{"script:ld+json":{"@context":"https://schema.org","@type":"JobPosting",
          "title":"Backend Engineer","datePosted":"2026-09-25",
          "hiringOrganization":{"@type":"Organization","name":"Six Robotics"},
          "jobLocation":{"@type":"Place","address":{"addressLocality":"Oslo","addressCountry":"NO"}},
          "description":"<p>We build C&#43;&#43; software.</p><p>Join 90&#43; engineers.</p>"}}</script>
        </head><body><main>
        <h1>Backend Engineer</h1>
        <article>Similar job: Frontend Developer at Other AS</article>
        <article>Similar job: Data Engineer at Third AS</article>
        </main></body></html>
        """;

    private static ListingPageFetcher FetcherReturning(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) })),
            NullLogger<ListingPageFetcher>.Instance);

    [Fact]
    public async Task UsesJobPosting_AndIgnoresRelatedListings()
    {
        var text = await FetcherReturning(HttpStatusCode.OK, PageWithJobPosting).GetListingTextAsync(Url, default);

        Assert.Contains("Title: Backend Engineer", text);
        Assert.Contains("Company: Six Robotics", text);
        Assert.Contains("Location: Oslo, NO", text);
        Assert.Contains("We build C++ software.", text);
        Assert.Contains("Join 90+ engineers.", text);
        Assert.DoesNotContain("<p>", text);
        Assert.DoesNotContain("Similar job", text);
        Assert.DoesNotContain("web page", text);
    }

    [Fact]
    public async Task ShownDeadline_ReplacesMadeUpValidThrough()
    {
        var page = PageWithJobPosting
            .Replace("\"datePosted\"", "\"validThrough\":\"2026-10-08\",\"datePosted\"")
            .Replace("<h1>", "<span>Søknadsfrist</span> <strong class=\"key-info-text\">snarest</strong><h1>");

        var text = await FetcherReturning(HttpStatusCode.OK, page).GetListingTextAsync(Url, default);

        Assert.Contains("Application deadline: snarest", text);
        Assert.DoesNotContain("2026-10-08", text);
    }

    [Fact]
    public async Task WithoutJobPosting_FallsBackToPageText_WithHint()
    {
        var body = string.Concat(Enumerable.Repeat("Real ad paragraph that is long enough to count as content. ", 10));
        var html = $"<html><body><nav>Home Jobs Login</nav><main><p>{body}</p></main><footer>Cookies</footer></body></html>";

        var text = await FetcherReturning(HttpStatusCode.OK, html).GetListingTextAsync(Url, default);

        Assert.StartsWith("Text extracted from a web page.", text);
        Assert.Contains("Real ad paragraph", text);
        Assert.DoesNotContain("Home Jobs Login", text);
        Assert.DoesNotContain("Cookies", text);
    }

    [Fact]
    public async Task PageWithNoReadableText_IsRejected()
    {
        var shell = "<html><body><div id=\"app\"></div><script>window.boot()</script></body></html>";

        var ex = await Assert.ThrowsAsync<InvalidParseRequestException>(
            () => FetcherReturning(HttpStatusCode.OK, shell).GetListingTextAsync(Url, default));

        Assert.Equal(ListingPageFetcher.Unreadable, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task NonSuccessStatus_IsRejected(HttpStatusCode status) =>
        await Assert.ThrowsAsync<InvalidParseRequestException>(
            () => FetcherReturning(status, "denied").GetListingTextAsync(Url, default));

    [Fact]
    public async Task OversizedBody_IsRejected() =>
        await Assert.ThrowsAsync<InvalidParseRequestException>(
            () => FetcherReturning(HttpStatusCode.OK, new string('a', 2_500_000)).GetListingTextAsync(Url, default));

    [Fact]
    public async Task Timeout_IsRejected()
    {
        var fetcher = new ListingPageFetcher(new HttpClient(new StubHandler(
            _ => throw new TaskCanceledException("timed out", new TimeoutException()))), NullLogger<ListingPageFetcher>.Instance);

        await Assert.ThrowsAsync<InvalidParseRequestException>(() => fetcher.GetListingTextAsync(Url, default));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:2800:220:1::1", true)]
    public void IsPublic_ClassifiesAddresses(string address, bool expected) =>
        Assert.Equal(expected, ListingPageFetcher.IsPublic(IPAddress.Parse(address)));

    // The real handler, not the stub: a request to a loopback server must never connect.
    [Fact]
    public async Task RealHandler_RefusesLoopbackTargets()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = new Uri($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/admin");
        var fetcher = new ListingPageFetcher(new HttpClient(ListingPageFetcher.CreateHandler()), NullLogger<ListingPageFetcher>.Instance);

        await Assert.ThrowsAsync<InvalidParseRequestException>(() => fetcher.GetListingTextAsync(url, default));

        Assert.False(server.Pending());
    }

    private class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
