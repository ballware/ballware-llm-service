namespace Ballware.Llm.Data.Ef.Configuration;

public sealed class ChatSessionStoreOptions
{
    public const int DefaultMaxHistoryMessages = 50;
    public const int MaxHistoryMessageLimit = 500;
    public const int DefaultSessionListLimit = 50;
    public const int MaxSessionListLimit = 500;

    public int MaxHistoryMessages { get; set; } = DefaultMaxHistoryMessages;

    public int SessionListLimit { get; set; } = DefaultSessionListLimit;
}
