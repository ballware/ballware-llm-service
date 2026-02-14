namespace Ballware.Llm.Engine.Ollama;

public interface IOllamaModelBridge
{
    IOllamaChat StartChat(OllamaChatOptions options, Guid tenantId, Guid userId, IDictionary<string, object> claims);
    Task<float[]> EmbedAsync(string chunk, string? model);
}