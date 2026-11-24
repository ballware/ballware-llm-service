namespace Ballware.Llm.App.Chat;

public sealed class ChatSessionOptions
{
    public const int DefaultMaxHistoryMessages = 50;
    public const int DefaultSessionListLimit = 50;

    public int MaxHistoryMessages { get; set; } = DefaultMaxHistoryMessages;

    public int SessionListLimit { get; set; } = DefaultSessionListLimit;
}
