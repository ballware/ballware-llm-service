namespace Ballware.Llm.Inference;

public interface IChatSession
{
    Task PromptAsync(string message, Action<string> tokenHandler);
}