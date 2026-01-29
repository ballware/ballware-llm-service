using System.Collections.Concurrent;

namespace Ballware.Llm.Api.Chat;

public class ChatSessionStore
{   
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    public void Add(ChatSession session)
    {
        _sessions[session.ConnectionId] = session;
    }
    
    public void Drop(string connectionId)
    {
        _sessions.TryRemove(connectionId, out _);
    }
    
    public ChatSession Get(string connectionId)
    {
        var session = _sessions.GetValueOrDefault(connectionId);
        
        if (session == null)
        {
            throw new KeyNotFoundException($"Chat session with connection ID '{connectionId}' not found.");
        }
        
        return session;
    }
}