namespace Ballware.Llm.Engine.Ollama;

public interface IAiModelBridge
{
    Task<IAiChat> StartChatAsync(IServiceProvider serviceProvider, AiChatOptions options, Guid tenantId, Guid userId, IDictionary<string, object> claims, string? userAccessToken);
    Task<float[]> EmbedAsync(string chunk, string? model);
}