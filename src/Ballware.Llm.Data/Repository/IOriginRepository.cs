using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IOriginRepository : ITenantableRepository<Public.Origin>
{
    Task<IEnumerable<Public.Origin>> GetGlobalOriginsAsync(Guid tenantId);
}