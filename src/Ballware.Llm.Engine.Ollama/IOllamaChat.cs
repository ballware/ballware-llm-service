namespace Ballware.Llm.Engine.Ollama;

public interface IOllamaChat
{
    Task PromptAsync(string message, Action<string> tokenHandler);
}