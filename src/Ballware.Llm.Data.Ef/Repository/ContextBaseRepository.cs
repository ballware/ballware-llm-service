using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using Microsoft.EntityFrameworkCore;
using MapsterMapper;

namespace Ballware.Llm.Data.Ef.Repository;

public class ContextBaseRepository(IMapper mapper, ILlmDbContext dbContext, IChatSessionRepository chatSessionRepository, ITenantableRepositoryHook<Public.Context, Persistables.Context>? hook = null) 
    : TenantableBaseRepository<Public.Context, Persistables.Context>(mapper, dbContext, hook), IContextRepository
{
    protected override async Task BeforeRemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Persistables.Context persistable)
    {
        await base.BeforeRemoveAsync(tenantId, userId, claims, persistable);

        await chatSessionRepository.DeleteByContextAsync(tenantId, persistable.Identifier);
    }

    public async Task<Public.Context?> GetByIdentifierAsync(Guid tenantId, string identifier)
    {
        var context = await dbContext.Set<Persistables.Context>().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Identifier == identifier);
        
        return context != null ? mapper.Map<Public.Context>(context) : null;
    }
}