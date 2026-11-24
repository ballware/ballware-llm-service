namespace Ballware.Llm.Data;

public class ChatSessionNotVisibleException : Exception
{
    public Guid SessionId { get; }

    public ChatSessionNotVisibleException(Guid sessionId)
        : base($"Chat session {sessionId} does not exist or is not accessible for the acting user.")
    {
        SessionId = sessionId;
    }

    public ChatSessionNotVisibleException(Guid sessionId, string message)
        : base(message)
    {
        SessionId = sessionId;
    }
}
