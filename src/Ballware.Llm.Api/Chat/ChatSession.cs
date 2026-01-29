using Ballware.Llm.Ollama;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, IOllamaChat chat)
{
    public string ConnectionId { get; } = connectionId;
    
    public IOllamaChat Chat { get; } = chat;
}