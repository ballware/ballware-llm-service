namespace Ballware.Assistant;

public interface IAssistantSession
{
    Task PromptAsync(string message, Action<string> tokenHandler);
}
