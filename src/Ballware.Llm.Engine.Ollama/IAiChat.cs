namespace Ballware.Llm.Engine.Ollama;

public interface IAiChat
{
    Task PromptAsync(string message, Action<string> tokenHandler);
}