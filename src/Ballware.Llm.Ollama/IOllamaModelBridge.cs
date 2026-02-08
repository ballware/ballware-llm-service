namespace Ballware.Llm.Ollama;

public interface IOllamaModelBridge
{
    IOllamaChat StartChat(OllamaChatOptions options);
    Task<float[]> EmbedAsync(string chunk, string? model);
}