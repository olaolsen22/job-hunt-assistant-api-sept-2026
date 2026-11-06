using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Api.Features.Parsing;

// Downloads a listing page and reduces it to the text of the one posting on it, or a company page to its text. The owner pastes arbitrary URLs and
// the server fetches them, so the handler built in CreateHandler is what keeps this off our own network.
public partial class ListingPageFetcher(HttpClient http, ILogger<ListingPageFetcher> logger)
{
    public const string Unreadable = "Couldn't read that page. Paste its text instead.";

    private const int MaxBytes = 2_000_000;
    private const int MaxRedirects = 5;

    // A JS-rendered shell or login wall leaves a few nav words; a real ad is far longer.
    private const int MinFallbackChars = 200;

    // Boards append related listings and menus, so the model is told which posting is the target.
    private const string FallbackHint =
        "Text extracted from a web page. It may include navigation and other listings; the first job posting is the target.";

    private const string CompanyHint =
        "Text extracted from a company's web page, including its header and footer. Email and phone links are written out.";

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = MaxRedirects,
        AutomaticDecompression = DecompressionMethods.All,
        // A proxy would do the connecting, and the check below could never see the real address.
        UseProxy = false,
        // Runs for every connection, including each redirect hop, and connects to the exact address it checked,
        // so a redirect or a hostname that flips to a private address can't get past it.
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var address = addresses.FirstOrDefault(IsPublic)
                ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} doesn't resolve to a public address.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast || address.Equals(IPAddress.IPv6Any));

        var b = address.GetAddressBytes();
        return !(b[0] is 0 or 10 or >= 224
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168));
    }

    // Throws InvalidParseRequestException (a typed mutation error) for anything that stops us reading the page. The
    // owner only sees one message for all of them, so the real reason goes to the log. Host only: query strings
    // can carry tokens.
    public async Task<string> GetListingTextAsync(Uri url, CancellationToken ct) =>
        Readable(Extract(await GetHtmlAsync(url, ct)), url);

    // A company's about, team or contact page. Contact details often sit in the footer or only in mailto:/tel: links,
    // so those stay; only scripts, styles and menus go.
    public async Task<string> GetCompanyTextAsync(Uri url, CancellationToken ct)
    {
        var html = await GetHtmlAsync(url, ct);
        var text = HtmlToText(PageCode().Replace(ContactLink().Replace(html, " $1 "), " "));
        return Readable(text.Length < MinFallbackChars ? null : $"{CompanyHint}\n\n{text}", url);
    }

    private string Readable(string? text, Uri url)
    {
        if (text is not null) return text;
        logger.LogWarning("Page on {Host} had no readable text (JS-rendered or login wall?)", url.Host);
        throw new InvalidParseRequestException(Unreadable);
    }

    private async Task<string> GetHtmlAsync(Uri url, CancellationToken ct)
    {
        string html;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Page on {Host} answered {StatusCode}", url.Host, (int)response.StatusCode);
                throw new InvalidParseRequestException(Unreadable);
            }
            html = await ReadCappedAsync(response, url, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Page on {Host} couldn't be fetched", url.Host);
            throw new InvalidParseRequestException(Unreadable);
        }
        // HttpClient reports its own timeout as TaskCanceledException; with our token still live it's the site being slow.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Page on {Host} timed out", url.Host);
            throw new InvalidParseRequestException(Unreadable);
        }
        return html;
    }

    // Counts bytes as they arrive instead of trusting Content-Length, which a server can omit or lie about.
    private async Task<string> ReadCappedAsync(HttpResponseMessage response, Uri url, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                logger.LogWarning("Page on {Host} is larger than {MaxBytes} bytes", url.Host, MaxBytes);
                throw new InvalidParseRequestException(Unreadable);
            }
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    // The page's JobPosting when it has one (only this ad, no menus or related listings), else its stripped text.
    private static string? Extract(string html)
    {
        foreach (Match script in JsonLdScript().Matches(html))
        {
            JsonDocument json;
            try
            {
                json = JsonDocument.Parse(script.Groups[1].Value);
            }
            catch (JsonException)
            {
                // Hand-written JSON-LD is often malformed; another block or the fallback may still work.
                continue;
            }
            using (json)
            {
                if (FindJobPosting(json.RootElement) is { } posting)
                    return DescribePosting(posting, html);
            }
        }

        var text = HtmlToText(BlockElement().Replace(html, " "));
        return text.Length < MinFallbackChars ? null : $"{FallbackHint}\n\n{text}";
    }

    // Recursive because sites wrap it: finn.no nests it under a "script:ld+json" key, others use @graph or arrays.
    private static JsonElement? FindJobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (Text(element, "@type") == "JobPosting")
                    return element;
                foreach (var property in element.EnumerateObject())
                    if (FindJobPosting(property.Value) is { } found)
                        return found;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindJobPosting(item) is { } found)
                        return found;
                break;
        }
        return null;
    }

    private static string DescribePosting(JsonElement posting, string html)
    {
        var lines = new List<string>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{label}: {value}");
        }

        Add("Title", Text(posting, "title"));
        if (posting.TryGetProperty("hiringOrganization", out var organization))
            Add("Company", organization.ValueKind == JsonValueKind.Object ? Text(organization, "name") : organization.GetString());
        Add("Location", Location(posting));
        Add("Posted", Text(posting, "datePosted"));
        // finn.no fills validThrough with a made-up date when the ad says "snarest", so the wording on the page wins.
        if (ShownDeadline().Match(html) is { Success: true } shown)
            Add("Application deadline", HtmlToText(shown.Groups[1].Value));
        else
            Add("Valid through", Text(posting, "validThrough"));
        Add("Employment type", Text(posting, "employmentType"));
        lines.Add("");
        lines.Add(HtmlToText(Text(posting, "description") ?? ""));
        return string.Join('\n', lines);
    }

    // jobLocation is one Place or an array of them; the address is nested and every part is optional.
    private static string? Location(JsonElement posting)
    {
        if (!posting.TryGetProperty("jobLocation", out var location))
            return null;
        var places = location.ValueKind == JsonValueKind.Array ? location.EnumerateArray().ToList() : [location];
        var names = places
            .Where(p => p.ValueKind == JsonValueKind.Object && p.TryGetProperty("address", out _))
            .Select(p => p.GetProperty("address"))
            .Where(a => a.ValueKind == JsonValueKind.Object)
            .Select(a => string.Join(", ", new[] { Text(a, "addressLocality"), Text(a, "addressRegion"), Text(a, "addressCountry") }
                .Where(part => !string.IsNullOrWhiteSpace(part))))
            .Where(name => name.Length > 0);
        return string.Join("; ", names);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string HtmlToText(string html)
    {
        var text = LineBreakTag().Replace(html, "\n");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    [GeneratedRegex("""<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdScript();

    // Navigation, footers and the like: never part of the posting itself.
    [GeneratedRegex(@"<!--.*?-->|<(script|style|noscript|svg|nav|header|footer|aside)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockElement();

    [GeneratedRegex(@"<!--.*?-->|<(script|style|noscript|svg|nav)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PageCode();

    // The opening <a> of a mailto:/tel: link, replaced by the address so it survives as text.
    [GeneratedRegex("""<a\b[^>]*\bhref\s*=\s*["'](?:mailto|tel):([^"'?]+)[^"']*["'][^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex ContactLink();

    [GeneratedRegex(@"Søknadsfrist</span>\s*<strong[^>]*>(.*?)</strong>", RegexOptions.Singleline)]
    private static partial Regex ShownDeadline();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|ul|ol|h[1-6]|tr|section|article)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*(\n\s*)+")]
    private static partial Regex BlankLines();
}
