namespace Ballware.Llm.Inference;

public interface IChatSession
{
    IReadOnlyList<ChatMessage> History { get; }

    IAsyncEnumerable<string> PromptStreamingAsync(
        string message,
        CancellationToken cancellationToken = default);

    Task PromptAsync(
        string message,
        Action<string> tokenHandler,
        CancellationToken cancellationToken = default);
}
