using Ballware.Llm.Data.Ef.Configuration;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Data.Ef.Repository;

public class ChatSessionBaseRepository(
    IMapper mapper,
    ILlmDbContext dbContext,
    ChatSessionStoreOptions? storeOptions = null,
    IChatSessionActorResolver? actorResolver = null,
    Microsoft.Extensions.Logging.ILogger<ChatSessionBaseRepository>? logger = null)
    : TenantableBaseRepository<Public.ChatSession, Persistables.ChatSession>(mapper, dbContext, null), IChatSessionRepository
{
    private static readonly IDictionary<string, object> EmptyClaims = new Dictionary<string, object>();

    private static readonly string[] UserIdClaimKeys = ["sub", "nameid", "userId", "user_id"];
    private static readonly string[] TenantIdClaimKeys = ["tenant", "tenant_id", "tenantId"];

    private ChatSessionActor? cachedActor;
    private Guid cachedActorUserId;

    private int MaxHistoryMessages =>
        Math.Clamp(storeOptions?.MaxHistoryMessages ?? ChatSessionStoreOptions.DefaultMaxHistoryMessages, 1, ChatSessionStoreOptions.MaxHistoryMessageLimit);

    private int SessionListLimit =>
        Math.Clamp(storeOptions?.SessionListLimit ?? ChatSessionStoreOptions.DefaultSessionListLimit, 1, ChatSessionStoreOptions.MaxSessionListLimit);

    protected override IQueryable<Persistables.ChatSession> ListQuery(IQueryable<Persistables.ChatSession> query, string identifier,
        IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        query = base.ListQuery(query, identifier, claims, queryParams);

        var actor = ResolveActorSync(TryClaimGuid(claims, TenantIdClaimKeys) ?? Guid.Empty, null, claims);

        query = actor.IsAdmin
            ? query
            : query.Where(session => session.UserId == actor.UserId);

        if (queryParams.TryGetValue("context", out var contextParam))
        {
            var contextIdentifier = contextParam?.ToString();

            if (!string.IsNullOrWhiteSpace(contextIdentifier))
            {
                query = query.Where(session => session.ContextIdentifier == contextIdentifier);
            }
        }

        return query.OrderByDescending(session => session.LastChangeStamp).ThenBy(session => session.Uuid);
    }

    protected override IQueryable<Persistables.ChatSession> ByIdQuery(IQueryable<Persistables.ChatSession> query, string identifier,
        IDictionary<string, object> claims, Guid tenantId, Guid id)
    {
        query = base.ByIdQuery(query, identifier, claims, tenantId, id);

        var actor = ResolveActorSync(tenantId, null, claims);

        return actor.IsAdmin
            ? query
            : query.Where(session => session.UserId == actor.UserId);
    }

    protected override async Task<Persistables.ChatSession> ProduceNewAsync(string identifier, IDictionary<string, object> claims,
        IDictionary<string, object>? queryParams, Guid tenantId)
    {
        var instance = await base.ProduceNewAsync(identifier, claims, queryParams, tenantId);

        var actor = await ResolveActorAsync(tenantId, null, claims);

        instance.UserId = actor.UserId;
        instance.ContextIdentifier = TryQueryValue(queryParams, "context") ?? identifier;

        return instance;
    }

    protected override async Task<Public.ChatSession> ExtendByIdAsync(string identifier, IDictionary<string, object> claims, Guid tenantId,
        Public.ChatSession value)
    {
        value = await base.ExtendByIdAsync(identifier, claims, tenantId, value);

        value.Messages = await LoadMessagesAsync(tenantId, value.Id, MaxHistoryMessages);

        return value;
    }

    public override async Task SaveAsync(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims,
        Public.ChatSession value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var actor = await ResolveActorAsync(tenantId, userId, claims);

        var persistedItem = await dbContext.Set<Persistables.ChatSession>()
            .FirstOrDefaultAsync(session => session.TenantId == tenantId && session.Uuid == value.Id);

        if (persistedItem == null || (!actor.IsAdmin && persistedItem.UserId != actor.UserId))
        {
            throw new ChatSessionNotVisibleException(value.Id);
        }

        // Only the title is writable: context and history remain owned by the persisted row.
        var title = NormalizeTitle(value.Title);

        if (title != null)
        {
            persistedItem.Title = title;
            persistedItem.LastChangerId = userId;
            persistedItem.LastChangeStamp = DateTime.Now;

            dbContext.Set<Persistables.ChatSession>().Update(persistedItem);

            await dbContext.SaveChangesAsync();
        }
    }

    public override async Task<RemoveResult<Public.ChatSession>> RemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        IDictionary<string, object> removeParams)
    {
        if (!removeParams.TryGetValue("Id", out var idParam) || !Guid.TryParse(idParam?.ToString(), out var sessionId))
        {
            return new RemoveResult<Public.ChatSession>()
            {
                Result = false,
                Messages = ["Session id is required."],
                Value = null
            };
        }

        await RequireVisibleAsync(tenantId, userId, sessionId, claims);

        return await base.RemoveAsync(tenantId, userId, claims, removeParams);
    }

    protected override async Task BeforeRemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        Persistables.ChatSession persistable)
    {
        await base.BeforeRemoveAsync(tenantId, userId, claims, persistable);

        await dbContext.Set<Persistables.ChatMessage>()
            .Where(message => message.TenantId == tenantId && message.SessionId == persistable.Uuid)
            .ExecuteDeleteAsync();
    }

    public async Task<IEnumerable<Public.ChatSession>> ListByUserAsync(Guid tenantId, Guid userId, string contextIdentifier, int limit = 50)
    {
        var sessions = await dbContext.Set<Persistables.ChatSession>()
            .Where(session => session.TenantId == tenantId && session.UserId == userId && session.ContextIdentifier == contextIdentifier)
            .OrderByDescending(session => session.LastChangeStamp)
            .ThenBy(session => session.Uuid)
            .Take(Math.Clamp(limit, 1, ChatSessionStoreOptions.MaxSessionListLimit))
            .ToListAsync();

        return Mapper.Map<IEnumerable<Public.ChatSession>>(sessions);
    }

    public async Task<Public.ChatSession?> ByIdForUserAsync(Guid tenantId, Guid userId, Guid sessionId)
    {
        var persistedItem = await dbContext.Set<Persistables.ChatSession>()
            .FirstOrDefaultAsync(session => session.TenantId == tenantId && session.UserId == userId && session.Uuid == sessionId);

        return persistedItem == null ? null : Mapper.Map<Public.ChatSession>(persistedItem);
    }

    public async Task<Public.ChatSession> CreateAsync(Guid tenantId, Guid userId, string contextIdentifier, string? title)
    {
        var stamp = DateTime.Now;

        var session = new Persistables.ChatSession
        {
            TenantId = tenantId,
            Uuid = Guid.NewGuid(),
            UserId = userId,
            ContextIdentifier = contextIdentifier,
            Title = NormalizeTitle(title),
            CreatorId = userId,
            CreateStamp = stamp,
            LastChangerId = userId,
            LastChangeStamp = stamp
        };

        dbContext.Set<Persistables.ChatSession>().Add(session);

        await dbContext.SaveChangesAsync();

        return Mapper.Map<Public.ChatSession>(session);
    }

    public async Task RenameAsync(Guid tenantId, Guid userId, Guid sessionId, string title)
    {
        var persistedItem = await RequireVisibleAsync(tenantId, userId, sessionId, EmptyClaims);

        var normalized = NormalizeTitle(title);

        if (normalized == null)
        {
            return;
        }

        persistedItem.Title = normalized;
        persistedItem.LastChangerId = userId;
        persistedItem.LastChangeStamp = DateTime.Now;

        dbContext.Set<Persistables.ChatSession>().Update(persistedItem);

        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteForUserAsync(Guid tenantId, Guid userId, Guid sessionId)
    {
        var persistedItem = await RequireVisibleAsync(tenantId, userId, sessionId, EmptyClaims);

        await dbContext.Set<Persistables.ChatMessage>()
            .Where(message => message.TenantId == tenantId && message.SessionId == sessionId)
            .ExecuteDeleteAsync();

        dbContext.Set<Persistables.ChatSession>().Remove(persistedItem);

        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteByContextAsync(Guid tenantId, string contextIdentifier)
    {
        var sessionIds = await dbContext.Set<Persistables.ChatSession>()
            .Where(session => session.TenantId == tenantId && session.ContextIdentifier == contextIdentifier)
            .Select(session => session.Uuid)
            .ToListAsync();

        if (sessionIds.Count == 0)
        {
            return;
        }

        await dbContext.Set<Persistables.ChatMessage>()
            .Where(message => message.TenantId == tenantId && sessionIds.Contains(message.SessionId))
            .ExecuteDeleteAsync();

        await dbContext.Set<Persistables.ChatSession>()
            .Where(session => session.TenantId == tenantId && session.ContextIdentifier == contextIdentifier)
            .ExecuteDeleteAsync();
    }

    public async Task AppendMessagesAsync(Guid tenantId, Guid userId, Guid sessionId, IEnumerable<Public.ChatSessionMessage> messages)
    {
        var turns = (messages ?? [])
            .Where(message => message != null && !string.IsNullOrEmpty(message.Content) && !string.IsNullOrEmpty(message.Role))
            .ToList();

        if (turns.Count == 0)
        {
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            var persistedItem = await RequireVisibleAsync(tenantId, userId, sessionId, EmptyClaims);

            var sequence = await dbContext.Set<Persistables.ChatMessage>()
                .Where(message => message.TenantId == tenantId && message.SessionId == sessionId)
                .Select(message => (int?)message.Sequence)
                .MaxAsync() ?? 0;

            var stamp = DateTime.Now;
            var appended = new List<Persistables.ChatMessage>();

            foreach (var turn in turns)
            {
                appended.Add(new Persistables.ChatMessage
                {
                    TenantId = tenantId,
                    Uuid = Guid.NewGuid(),
                    SessionId = sessionId,
                    Role = turn.Role,
                    Content = turn.Content,
                    Sequence = ++sequence,
                    CreatorId = userId,
                    CreateStamp = stamp,
                    LastChangerId = userId,
                    LastChangeStamp = stamp
                });
            }

            dbContext.Set<Persistables.ChatMessage>().AddRange(appended);

            persistedItem.LastChangerId = userId;
            persistedItem.LastChangeStamp = stamp;

            dbContext.Set<Persistables.ChatSession>().Update(persistedItem);

            try
            {
                await dbContext.SaveChangesAsync();

                return;
            }
            catch (DbUpdateException ex)
            {
                (dbContext as DbContext)?.ChangeTracker.Clear();

                if (attempt >= 3)
                {
                    throw;
                }

                logger?.LogWarning(
                    ex,
                    "Chat message append conflict for session {SessionId} (attempt {Attempt}); retrying with a recalculated sequence.",
                    sessionId,
                    attempt);
            }
        }
    }

    public async Task<IEnumerable<Public.ChatSessionMessage>> GetMessagesAsync(Guid tenantId, Guid userId, Guid sessionId, int maxCount)
    {
        await RequireVisibleAsync(tenantId, userId, sessionId, EmptyClaims);

        return await LoadMessagesAsync(tenantId, sessionId, maxCount);
    }

    private async Task<List<Public.ChatSessionMessage>> LoadMessagesAsync(Guid tenantId, Guid sessionId, int maxCount)
    {
        var limit = Math.Clamp(maxCount, 1, ChatSessionStoreOptions.MaxHistoryMessageLimit);

        var messages = await dbContext.Set<Persistables.ChatMessage>()
            .Where(message => message.TenantId == tenantId && message.SessionId == sessionId)
            .OrderByDescending(message => message.Sequence)
            .Take(limit)
            .ToListAsync();

        return messages
            .AsEnumerable()
            .Reverse()
            .Select(message => new Public.ChatSessionMessage
            {
                Role = message.Role,
                Content = message.Content,
                Sequence = message.Sequence,
                CreateStamp = message.CreateStamp
            })
            .ToList();
    }

    private async Task<Persistables.ChatSession> RequireVisibleAsync(Guid tenantId, Guid? userId, Guid sessionId, IDictionary<string, object> claims)
    {
        var actor = await ResolveActorAsync(tenantId, userId, claims);

        var persistedItem = await dbContext.Set<Persistables.ChatSession>()
            .FirstOrDefaultAsync(session => session.TenantId == tenantId && session.Uuid == sessionId);

        if (persistedItem == null || (!actor.IsAdmin && persistedItem.UserId != actor.UserId))
        {
            throw new ChatSessionNotVisibleException(sessionId);
        }

        return persistedItem;
    }

    private async Task<ChatSessionActor> ResolveActorAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims)
    {
        var effectiveUserId = userId ?? TryClaimGuid(claims, UserIdClaimKeys) ?? Guid.Empty;

        if (cachedActor != null && cachedActorUserId == effectiveUserId)
        {
            return cachedActor;
        }

        var actor = actorResolver != null
            ? await actorResolver.ResolveActorAsync(tenantId, effectiveUserId, claims)
            : new ChatSessionActor(effectiveUserId, false);

        if (actor.UserId == Guid.Empty)
        {
            actor = actor with { UserId = effectiveUserId };
        }

        cachedActor = actor;
        cachedActorUserId = effectiveUserId;

        return actor;
    }

    private ChatSessionActor ResolveActorSync(Guid tenantId, Guid? userId, IDictionary<string, object> claims)
    {
        return ResolveActorAsync(tenantId, userId, claims).GetAwaiter().GetResult();
    }

    private static string? TryQueryValue(IDictionary<string, object>? queryParams, string key)
    {
        if (queryParams == null || !queryParams.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static Guid? TryClaimGuid(IDictionary<string, object> claims, params string[] claimKeys)
    {
        if (claims == null)
        {
            return null;
        }

        foreach (var claimKey in claimKeys)
        {
            if (!claims.TryGetValue(claimKey, out var value) || value == null)
            {
                continue;
            }

            if (value is IEnumerable<string> values)
            {
                foreach (var candidate in values)
                {
                    if (Guid.TryParse(candidate, out var parsed))
                    {
                        return parsed;
                    }
                }

                continue;
            }

            if (Guid.TryParse(value.ToString(), out var guid))
            {
                return guid;
            }
        }

        return null;
    }

    private static string? NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var normalized = title.Trim();

        return normalized.Length > 200 ? normalized[..200] : normalized;
    }
}
