using Ballware.Llm.Context;
using Ballware.Llm.Engine.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, string contextIdentifier, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken)
{
    public string ConnectionId { get; } = connectionId;
    
    public IAiChat? Chat { get; private set; }
    
    public async Task StartChatAsync(IServiceProvider serviceProvider, IAiModelBridge modelBridge, IModelContextProvider modelContextProvider, string initialPrompt)
    {
        var logger = serviceProvider.GetService<ILogger<ChatSession>>();
        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);

        logger?.LogDebug(
            "LLM system prompt (chat start, estimated tokens: {EstimatedSystemPromptTokens}): {SystemPrompt}",
            EstimateTokenCount(systemPrompt),
            systemPrompt);
        
        Chat = await modelBridge.StartChatAsync(serviceProvider, new AiChatOptions()
        {
            Model = options.Model,
            NumCtx = options.NumCtx,
            SystemPrompt = systemPrompt
        }, tenantId, userId, claims, userAccessToken);
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }
}
