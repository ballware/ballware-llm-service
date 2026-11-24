namespace Ballware.Llm.Data.Repository;

public interface IChatSessionActorResolver
{
    Task<ChatSessionActor> ResolveActorAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims);
}
