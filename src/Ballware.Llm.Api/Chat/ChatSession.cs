using Ballware.Llm.Context;
using Ballware.Llm.Engine.Ollama;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, string contextIdentifier, Guid tenantId, Guid userId, IDictionary<string, object> claims)
{
    public string ConnectionId { get; } = connectionId;
    
    public IOllamaChat? Chat { get; private set; }
    
    public async Task StartChatAsync(IOllamaModelBridge modelBridge, IModelContextProvider modelContextProvider, string initialPrompt)
    {
        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);
        
        Chat = modelBridge.StartChat(new OllamaChatOptions()
        {
            Model = options.Model,
            NumCtx = options.NumCtx,
            SystemPrompt = systemPrompt
        }, tenantId, userId, claims);
    }
}