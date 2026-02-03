namespace Ballware.Llm.Ollama;

public interface IOllamaModelBridge
{
    IOllamaChat StartChat();
    Task<float[]> EmbedAsync(string chunk, string? model);
}