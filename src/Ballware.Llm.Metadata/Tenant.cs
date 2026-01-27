using Ballware.Shared.Authorization;

namespace Ballware.Llm.Metadata;

public class Tenant : ITenantAuthorizationMetadata
{
    public Guid Id { get; set; }
    public string? RightsCheckScript { get; set; }
}