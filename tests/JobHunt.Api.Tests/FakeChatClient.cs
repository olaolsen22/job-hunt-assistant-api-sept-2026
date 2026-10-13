using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests;

// Stands in for Gemini: a test sets what the "model" answers and checks what it was sent.
public class FakeChatClient : IChatClient
{
    public Func<ChatResponse> Respond { get; set; } = () => throw new InvalidOperationException("No fake LLM response set.");
    public Func<CancellationToken, Task<ChatResponse>>? RespondAsync { get; set; }
    public List<ChatMessage> LastMessages { get; private set; } = [];
    public ChatOptions? LastOptions { get; private set; }
    public int Calls { get; private set; }
    public bool Disposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        LastMessages = messages.ToList();
        LastOptions = options;
        return RespondAsync?.Invoke(cancellationToken) ?? Task.FromResult(Respond());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { Disposed = true; }
}
