using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Path = System.IO.Path;

namespace JobHunt.Api.Features.ListingImports;

// Polls <Folder>/<account email>/*.json and stores each new file as a pending import for that account.
// Polling rather than FileSystemWatcher: watchers are unreliable on WSL and fire before a file is fully written.
// Files are only read, never moved or deleted; the stored file name is what marks one as imported.
public partial class ListingImportScanner(
    IServiceScopeFactory scopes,
    IOptions<ListingImportOptions> options,
    IHostEnvironment environment,
    TimeProvider time,
    ILogger<ListingImportScanner> logger) : BackgroundService
{
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxItems = 500;
    // A file this recently written may still be in progress; the next scan picks it up.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(5);

    // The loop and a test calling ScanOnceAsync must not import the same file twice.
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Warned once per path, so a stray file doesn't log every minute.
    private readonly HashSet<string> _warned = [];

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})(?:_(\d{2}-\d{2}-\d{2}))?-([a-z][a-z-]{0,48})\.json$")]
    private static partial Regex FileNamePattern();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds)), time);
        do
        {
            // One bad scan (locked DB, unreadable folder) must not stop the loop for the app's lifetime.
            try { await ScanOnceAsync(stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Listing import scan failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task ScanOnceAsync(CancellationToken ct = default)
    {
        var root = Path.Combine(environment.ContentRootPath, options.Value.Folder);
        if (!Directory.Exists(root))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var email = Path.GetFileName(folder);
                var user = await users.FindByEmailAsync(email);
                if (user is null)
                {
                    WarnOnce(folder, "Listing import folder {Folder} doesn't match an account email; skipped.");
                    continue;
                }
                await ScanUserFolderAsync(folder, user.Id, db, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ScanUserFolderAsync(string folder, string userId, AppDbContext db, CancellationToken ct)
    {
        // No signed-in user here, so the user filter would hide every row; filter by UserId by hand.
        var known = (await db.ListingImports.IgnoreQueryFilters()
            .Where(i => i.UserId == userId)
            .Select(i => i.FileName)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var now = time.GetUtcNow().UtcDateTime;

        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            var fileName = Path.GetFileName(path);
            if (known.Contains(fileName))
                continue;
            if (!TryParseFileName(fileName, out var generatedAt, out var kind))
            {
                WarnOnce(path, "Listing import file {Folder} isn't named <date>[_<time>]-<kind>.json; skipped.");
                continue;
            }
            var file = new FileInfo(path);
            if (now - file.LastWriteTimeUtc < SettleTime)
                continue;

            var (content, count, error) = await ReadAsync(file, ct);
            db.ListingImports.Add(new ListingImport
            {
                UserId = userId,
                FileName = fileName,
                Kind = kind,
                GeneratedAt = generatedAt,
                ImportedAt = now,
                ItemCount = count,
                Content = content,
                Error = error,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // The name carries the generator's local time; stored as UTC like every other timestamp.
    private bool TryParseFileName(string fileName, out DateTime generatedAt, out string kind)
    {
        generatedAt = default;
        kind = "";
        var match = FileNamePattern().Match(fileName);
        if (!match.Success)
            return false;
        var stamp = match.Groups[1].Value + " " + (match.Groups[2].Success ? match.Groups[2].Value : "00-00-00");
        if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;
        // A time the clocks skipped at a DST change has no UTC value, and the throw would stop every later scan.
        if (time.LocalTimeZone.IsInvalidTime(local))
            local = local.AddHours(1);
        generatedAt = TimeZoneInfo.ConvertTimeToUtc(local, time.LocalTimeZone);
        kind = match.Groups[3].Value;
        return true;
    }

    // Same checks as quick add's JSON input, so a stored file loads there without surprises.
    private static async Task<(string? Content, int Count, string? Error)> ReadAsync(FileInfo file, CancellationToken ct)
    {
        if (file.Length > MaxFileBytes)
            return (null, 0, $"File is larger than {MaxFileBytes / 1024 / 1024} MB.");
        var content = await File.ReadAllTextAsync(file.FullName, ct);

        JsonDocument document;
        try { document = JsonDocument.Parse(content); }
        catch (JsonException) { return (null, 0, "File isn't valid JSON."); }

        using (document)
        {
            var items = document.RootElement;
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                return (null, 0, "File must be a non-empty JSON array of listings.");
            var count = items.GetArrayLength();
            if (count > MaxItems)
                return (null, count, $"File has {count} listings; the limit is {MaxItems}.");
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("url", out var url)
                    || url.ValueKind != JsonValueKind.String
                    || !Inputs.TryHttpUrl(url.GetString()!.Trim(), out _))
                    return (null, count, $"Listing {index} needs an http(s) url.");
            }
            return (content, count, null);
        }
    }

    private void WarnOnce(string path, string message)
    {
        if (_warned.Add(path))
            logger.LogWarning(message, path);
    }
}
