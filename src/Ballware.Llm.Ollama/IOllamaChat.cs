namespace Ballware.Llm.Ollama;

public interface IOllamaChat
{
    Task PromptAsync(string message, Action<string> tokenHandler);
}