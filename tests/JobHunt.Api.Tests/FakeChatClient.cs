using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests;

// Stands in for Gemini: a test sets what the "model" answers and checks what it was sent.
public class FakeChatClient : IChatClient
{
    public Func<ChatResponse> Respond { get; set; } = () => throw new InvalidOperationException("No fake LLM response set.");
    public List<ChatMessage> LastMessages { get; private set; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        return Task.FromResult(Respond());
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
