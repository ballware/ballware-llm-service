using Ballware.Llm.Data.Repository;
using Ballware.Llm.Hosting.Configuration;
using Ballware.Shared.Authorization;
using Microsoft.Extensions.Logging;

namespace Ballware.Llm.Hosting.Adapter;

public class MetaServiceChatSessionActorResolver(
    LlmAuthorizationClaimsOptions claimsOptions,
    IAuthorizationMetadataProvider authorizationMetadataProvider,
    ITenantRightsChecker tenantRightsChecker,
    IEntityRightsChecker entityRightsChecker,
    ILogger<MetaServiceChatSessionActorResolver> logger) : IChatSessionActorResolver
{
    public const string Application = "ai";
    public const string Entity = "session";
    public const string AdminRight = "admin";

    public async Task<ChatSessionActor> ResolveActorAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims)
    {
        var resolvedUserId = userId ?? Guid.Empty;

        if (resolvedUserId == Guid.Empty)
        {
            resolvedUserId = ResolveUserId(claims) ?? Guid.Empty;
        }

        var isAdmin = tenantId != Guid.Empty && await HasAdminRightAsync(tenantId, claims);

        return new ChatSessionActor(resolvedUserId, isAdmin);
    }

    private async Task<bool> HasAdminRightAsync(Guid tenantId, IDictionary<string, object> claims)
    {
        try
        {
            var tenantMetadata = await authorizationMetadataProvider.MetadataForTenantByIdAsync(tenantId);

            if (tenantMetadata == null)
            {
                logger.LogDebug("No tenant metadata for tenant {TenantId}; chat session admin right denied.", tenantId);

                return false;
            }

            var entityMetadata = await authorizationMetadataProvider.MetadataForEntityByTenantAndIdentifierAsync(tenantId, Entity);

            if (entityMetadata == null)
            {
                logger.LogDebug(
                    "No entity metadata for entity {Entity} in tenant {TenantId}; chat session admin right denied.",
                    Entity,
                    tenantId);

                return false;
            }

            var tenantAllowed = await tenantRightsChecker.HasRightAsync(tenantMetadata, Application, Entity, claims, AdminRight);

            return await entityRightsChecker.HasRightAsync(tenantId, entityMetadata, claims, AdminRight, null, tenantAllowed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Chat session admin right check failed for tenant {TenantId}; treated as denied.",
                tenantId);

            return false;
        }
    }

    private Guid? ResolveUserId(IDictionary<string, object> claims)
    {
        if (claims == null)
        {
            return null;
        }

        foreach (var claimKey in new[] { claimsOptions.UserIdClaim, "sub", "nameid" })
        {
            if (string.IsNullOrEmpty(claimKey))
            {
                continue;
            }

            if (claims.TryGetValue(claimKey, out var value))
            {
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

                if (Guid.TryParse(value?.ToString(), out var guid))
                {
                    return guid;
                }
            }
        }

        return null;
    }
}
