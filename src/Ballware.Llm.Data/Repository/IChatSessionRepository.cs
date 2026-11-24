using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IChatSessionRepository : ITenantableRepository<Public.ChatSession>
{
    Task<IEnumerable<Public.ChatSession>> ListByUserAsync(Guid tenantId, Guid userId, string contextIdentifier, int limit = 50);

    Task<Public.ChatSession?> ByIdForUserAsync(Guid tenantId, Guid userId, Guid sessionId);

    Task<Public.ChatSession> CreateAsync(Guid tenantId, Guid userId, string contextIdentifier, string? title);

    Task RenameAsync(Guid tenantId, Guid userId, Guid sessionId, string title);

    Task DeleteForUserAsync(Guid tenantId, Guid userId, Guid sessionId);

    Task DeleteByContextAsync(Guid tenantId, string contextIdentifier);

    Task AppendMessagesAsync(Guid tenantId, Guid userId, Guid sessionId, IEnumerable<Public.ChatSessionMessage> messages);

    Task<IEnumerable<Public.ChatSessionMessage>> GetMessagesAsync(Guid tenantId, Guid userId, Guid sessionId, int maxCount);
}
