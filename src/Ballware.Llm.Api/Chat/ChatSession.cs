using Ballware.Llm.Context;
using Ballware.Llm.Engine.Ollama;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, string contextIdentifier, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken)
{
    public string ConnectionId { get; } = connectionId;
    
    public IAiChat? Chat { get; private set; }
    
    public async Task StartChatAsync(IServiceProvider serviceProvider, IAiModelBridge modelBridge, IModelContextProvider modelContextProvider, string initialPrompt)
    {
        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);
        
        Chat = await modelBridge.StartChatAsync(serviceProvider, new AiChatOptions()
        {
            Model = options.Model,
            NumCtx = options.NumCtx,
            SystemPrompt = systemPrompt
        }, tenantId, userId, claims, userAccessToken);
    }
}