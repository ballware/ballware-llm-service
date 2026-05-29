namespace Ballware.Assistant;

public interface IAssistantSessionFactory
{
    Task<IAssistantSession> StartSessionAsync(AssistantSessionStartOptions options, CancellationToken cancellationToken = default);
}
