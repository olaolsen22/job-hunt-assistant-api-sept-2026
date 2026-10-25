using System.ClientModel;
using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using Google.GenAI;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Usage;
using JobHunt.Api.Features.LlmProviderKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Llm;

// The public picker ID can differ from the provider's model ID; no secret is carried in this record.
public record ResolvedLlmModel(string Id, string Provider, string ModelId, string Label,
    Guid? UserProviderKeyId = null, int? RequestsPerDay = null)
{
    public LlmKeySource KeySource => UserProviderKeyId is null ? LlmKeySource.Shared : LlmKeySource.User;
    public string CredentialId => UserProviderKeyId?.ToString("D") ?? LlmProviders.SharedCredentialId;
}

public record LlmCallResult(ChatResponse? Response, LlmUsage? Usage, string? ErrorCode)
{
    public string? Error => ErrorCode is null ? null : ProviderKeyException.MessageFor(ErrorCode);
}

// Value is null exactly when Error is set: the provider failed, or its answer didn't decode.
public record LlmJsonResult<T>(T? Value, string? Error, string? ErrorCode) where T : class;

public class LlmExecution(
    [FromKeyedServices(GeminiOptions.Provider)] IChatClient shared,
    IHttpClientFactory http, ProviderKeyProtector protector, TimeProvider time, ILogger<LlmExecution> logger,
    IDbContextFactory<AppDbContext> contexts, AiOperations operations)
{
    public Task<LlmCallResult> RunAsync(ResolvedLlmModel model, IEnumerable<ChatMessage> messages,
        ChatOptions options, string operation, AppDbContext db, CancellationToken ct)
    {
        var input = messages.ToList();
        if (operations.Current is not null) return RunCoreAsync(model, input, options, operation, db, ct);
        return operations.RunAsync(operation, new { model.Id, Messages = input.Select(m => new { Role = m.Role.ToString(), m.Text }),
                options.MaxOutputTokens, options.Temperature }, null, db, () => Task.FromResult(model),
            selected => RunCoreAsync(selected, input, options, operation, db, ct),
            result => new AiOutcome(result.ErrorCode is null, result.ErrorCode), ct);
    }

    // For calls that must return JSON. decode returns null for an answer it can't use; that attempt's usage row is
    // then marked invalid-json. Usage is otherwise already saved by RunAsync.
    public async Task<LlmJsonResult<T>> RunJsonAsync<T>(ResolvedLlmModel model, IEnumerable<ChatMessage> messages,
        ChatOptions options, string operation, AppDbContext db, Func<string, T?> decode, string unreadable,
        CancellationToken ct) where T : class
    {
        var call = await RunAsync(model, messages, options, operation, db, ct);
        if (call.Error is { } error) return new(null, error, call.ErrorCode);
        if (decode(call.Response!.Text) is { } value) return new(value, null, null);
        call.Usage!.Success = false;
        call.Usage.ErrorCode = "invalid-json";
        await SaveUsageAsync(call.Usage, ct);
        return new(null, unreadable, "invalid-json");
    }

    private async Task<LlmCallResult> RunCoreAsync(ResolvedLlmModel model, IEnumerable<ChatMessage> messages,
        ChatOptions options, string operation, AppDbContext db, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IChatClient? personal = null;
        LlmUsage? usage = null;
        var start = time.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var client = shared;
            if (model.UserProviderKeyId is { } keyId)
            {
                var key = await db.UserProviderKeys.AsNoTracking().SingleOrDefaultAsync(k => k.Id == keyId && k.RetiredAt == null, ct)
                    ?? throw new ProviderKeyException("credential-unavailable");
                if (key.Provider != model.Provider) throw new ProviderKeyException("credential-unavailable");
                personal = ProviderClients.Create(model.Provider, protector.Unprotect(key), model.ModelId, http);
                client = personal;
            }
            else if (model.Provider != GeminiOptions.Provider) throw new ProviderKeyException("credential-unavailable");

            var mapped = options.Clone();
            mapped.ModelId = model.ModelId;
            if (model.Provider != GeminiOptions.Provider) mapped.Reasoning = null;
            // Anthropic's text adapter uses the shared strict-JSON instructions and decoder.
            if (model.Provider == LlmProviders.Anthropic) mapped.ResponseFormat = null;
            if (model.Provider == LlmProviders.Mistral && mapped.MaxOutputTokens is { } maximum)
            {
                // OpenAI's adapter otherwise emits max_completion_tokens, which Mistral doesn't accept.
                mapped.MaxOutputTokens = null;
                mapped.RawRepresentationFactory = _ =>
                {
                    var raw = new OpenAI.Chat.ChatCompletionOptions();
#pragma warning disable SCME0001 // Pinned SDK's raw JSON extension; covered by the Mistral transport test.
                    raw.Patch.Set("$.max_tokens"u8, maximum);
#pragma warning restore SCME0001
                    return raw;
                };
            }
            await operations.RecordWorkAsync(new { model.Provider, model.ModelId, model.CredentialId,
                Messages = messages.Select(m => new { Role = m.Role.ToString(), m.Text }),
                mapped.MaxOutputTokens, mapped.Temperature }, ct);
            usage = new LlmUsage
            {
                UserId = db.CurrentUserId!, Provider = model.Provider, Model = model.ModelId,
                KeySource = model.KeySource, UserProviderKeyId = model.UserProviderKeyId,
                AiOperationId = operations.Current!.Id, AttemptOrdinal = operations.NextOrdinal(),
                Operation = operation, CreatedAt = time.GetUtcNow().UtcDateTime, ErrorCode = "attempt-pending",
            };
            await ReserveAsync(usage, ct);
            var response = await client.GetResponseAsync(messages, mapped, timeout.Token);
            usage.InputTokens = response.Usage?.InputTokenCount;
            usage.OutputTokens = NormalizeOutput(model.Provider, response.Usage);
            usage.Success = true;
            usage.ErrorCode = null;
            return new(response, usage, null);
        }
        catch (ProviderKeyException ex) { return Failed(ex.Code); }
        catch (HttpRequestException ex)
        {
            var status = ex is ApiException google ? (HttpStatusCode)google.StatusCode : ex.StatusCode;
            return Failed(status is { } s ? ProviderKeyValidator.CodeFor(s) : "provider-error");
        }
        catch (ClientResultException ex) { return Failed(ProviderKeyValidator.CodeFor((HttpStatusCode)ex.Status)); }
        catch (AnthropicException ex)
        {
            return Failed(ex switch
            {
                AnthropicUnauthorizedException or AnthropicForbiddenException => "invalid-key",
                AnthropicRateLimitException => "rate-limited",
                _ => "provider-error",
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Failed("timeout"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (usage is not null) { usage.Success = false; usage.ErrorCode = "canceled"; }
            throw new OperationCanceledException(ct);
        }
        catch (JsonException) { return Failed("provider-error"); }
        catch (AiSafetyException) { usage = null; throw; }
        finally
        {
            personal?.Dispose();
            if (usage is not null)
            {
                usage.LatencyMs = (long)time.GetElapsedTime(start).TotalMilliseconds;
                await SaveUsageAsync(usage, CancellationToken.None);
            }
        }

        LlmCallResult Failed(string code)
        {
            if (usage is not null) { usage.Success = false; usage.ErrorCode = code; }
            // Never pass provider exceptions or response/request bodies to the logger.
            logger.LogWarning("LLM {Provider} {Operation} failed with {Code}", model.Provider, operation, code);
            return new(null, usage, code);
        }
    }

    private async Task ReserveAsync(LlmUsage usage, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        try
        {
            await using var transaction = await AiTransactions.BeginAsync(db, ct);
            if (usage.KeySource == LlmKeySource.User)
            {
                var limit = await db.UserLlmLimits.IgnoreQueryFilters()
                    .Where(l => l.UserId == usage.UserId && l.Provider == usage.Provider)
                    .Select(l => l.DailyRequestLimit).SingleOrDefaultAsync(ct);
                if (limit is { } maximum)
                {
                    var day = PacificDay.Of(usage.CreatedAt);
                    var start = PacificDay.StartUtc(day);
                    var end = PacificDay.StartUtc(day.AddDays(1));
                    var consumed = await db.LlmUsages.IgnoreQueryFilters().CountAsync(u => u.UserId == usage.UserId
                        && u.Provider == usage.Provider && u.KeySource == LlmKeySource.User
                        && u.CreatedAt >= start && u.CreatedAt < end, ct);
                    if (consumed >= maximum) throw new AiSafetyException("daily-limit-reached");
                }
            }
            db.LlmUsages.Add(usage);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (SqliteErrors.IsContention(ex)) { throw new AiSafetyException("ai-busy"); }
    }

    // Also called by decoders to mark invalid JSON; this context cannot save pending caller content.
    public async Task SaveUsageAsync(LlmUsage usage, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await db.LlmUsages.IgnoreQueryFilters().Where(u => u.Id == usage.Id && u.UserId == usage.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Success, usage.Success)
                .SetProperty(u => u.ErrorCode, usage.ErrorCode).SetProperty(u => u.InputTokens, usage.InputTokens)
                .SetProperty(u => u.OutputTokens, usage.OutputTokens).SetProperty(u => u.LatencyMs, usage.LatencyMs), ct);
    }

    public static long? NormalizeOutput(string provider, UsageDetails? usage) => usage?.OutputTokenCount is { } output
        ? provider == GeminiOptions.Provider ? output + (usage.ReasoningTokenCount ?? 0) : output
        : null;
}
