namespace Ballware.Llm.Ollama;

public interface IOllamaModelBridge
{
    IOllamaChat StartChat(string? systemPrompt = null);
    Task<float[]> EmbedAsync(string chunk, string? model);
}