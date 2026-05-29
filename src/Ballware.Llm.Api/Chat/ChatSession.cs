using Ballware.Assistant;
using Ballware.Llm.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Api.Chat;

public class ChatSession(string connectionId, string contextIdentifier, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken)
{
    public string ConnectionId { get; } = connectionId;
    
    public IAssistantSession? Chat { get; private set; }
    
    public async Task StartChatAsync(IServiceProvider serviceProvider, IAssistantSessionFactory assistantSessionFactory, IModelContextProvider modelContextProvider, string initialPrompt)
    {
        var logger = serviceProvider.GetService<ILogger<ChatSession>>();
        var options = await modelContextProvider.ProvideOptionsAsync(tenantId, contextIdentifier);
        var systemPrompt = await modelContextProvider.ProvideSystemPromptAsync(tenantId, contextIdentifier, initialPrompt);

        logger?.LogDebug(
            "LLM system prompt (chat start, estimated tokens: {EstimatedSystemPromptTokens}): {SystemPrompt}",
            EstimateTokenCount(systemPrompt),
            systemPrompt);
        
        Chat = await assistantSessionFactory.StartSessionAsync(new AssistantSessionStartOptions
        {
            TenantId = tenantId,
            UserId = userId,
            Claims = claims,
            UserAccessToken = userAccessToken,
            Model = options.Model,
            NumCtx = options.NumCtx,
            SystemPrompt = systemPrompt,
            Stages = options.AssistantStages
        });
    }

    private static long EstimateTokenCount(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : (text.Length + 3L) / 4L;
    }
}
