namespace Ballware.Llm.Inference;

public interface IInferenceEngine
{
    Task<IChatSession> StartChatAsync(IServiceProvider serviceProvider, ChatContextOptions contextOptions, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken);
}