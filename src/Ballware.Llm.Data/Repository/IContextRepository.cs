using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IContextRepository : ITenantableRepository<Public.Context>
{
    public Task<Public.Context?> GetByIdentifierAsync(Guid tenantId, string identifier);
}