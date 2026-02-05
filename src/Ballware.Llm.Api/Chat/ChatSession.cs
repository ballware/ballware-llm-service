using Ballware.Llm.Context;
using Ballware.Llm.Ollama;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, string contextIdentifier, Guid tenantId, Guid userId)
{
    public string ConnectionId { get; } = connectionId;
    
    public IOllamaChat? Chat { get; private set; }
    
    public async Task StartChatAsync(IOllamaModelBridge modelBridge, IModelContextProvider modelContextProvider, string initialPrompt)
    {
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);
        
        Chat = modelBridge.StartChat(systemPrompt);
    }
}