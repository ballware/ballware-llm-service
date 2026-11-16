using System.Security.Claims;

namespace Ballware.Llm.App.Chat;

public sealed class BallwareChatScope
{
    public const string ProviderServiceKey = "ballware-chat";

    public string? ContextIdentifier { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public ClaimsPrincipal? Principal { get; private set; }
    public string ConversationId { get; private set; } = Guid.NewGuid().ToString("N");

    public bool HasContext =>
        !string.IsNullOrWhiteSpace(ContextIdentifier) &&
        TenantId != Guid.Empty &&
        UserId != Guid.Empty &&
        Principal != null;

    public void Reset()
    {
        ContextIdentifier = null;
        TenantId = Guid.Empty;
        UserId = Guid.Empty;
        Principal = null;
        ConversationId = Guid.NewGuid().ToString("N");
    }

    public void Initialize(ClaimsPrincipal principal, Guid tenantId, Guid userId, string contextIdentifier)
    {
        Principal = principal;
        TenantId = tenantId;
        UserId = userId;
        ContextIdentifier = contextIdentifier;
        ConversationId = Guid.NewGuid().ToString("N");
    }
}
