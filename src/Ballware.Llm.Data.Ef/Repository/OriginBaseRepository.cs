using AutoMapper;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Ef.Repository;

public class OriginBaseRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Origin, Persistables.Origin>? hook = null) 
    : TenantableBaseRepository<Public.Origin, Persistables.Origin>(mapper, dbContext, hook), IOriginRepository
{
    
}