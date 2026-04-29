using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;

namespace Ballware.Llm.Data.Ef.Repository;

public class OriginBaseRepository(IMapper mapper, ILlmDbContext dbContext, IEmbeddingRepository embeddingRepository, IImageRepository imageRepository, ITenantableRepositoryHook<Public.Origin, Persistables.Origin>? hook = null) 
    : TenantableBaseRepository<Public.Origin, Persistables.Origin>(mapper, dbContext, hook), IOriginRepository
{
    protected override async Task BeforeRemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Persistables.Origin persistable)
    {
        await base.BeforeRemoveAsync(tenantId, userId, claims, persistable);
        
        var embeddings = (await embeddingRepository.QueryAsync(tenantId, "origin", claims, new Dictionary<string, object>
        {
            { "origin", persistable.Uuid }
        })).ToList();

        foreach (var embedding in embeddings)
        {
            await embeddingRepository.RemoveAsync(tenantId, userId, claims, new Dictionary<string, object>
            {
                { "Id", embedding.Id }
            });
        }

        var images = (await imageRepository.QueryAsync(tenantId, "origin", claims, new Dictionary<string, object>
        {
            { "origin", persistable.Uuid }
        })).ToList();

        foreach (var image in images)
        {
            await imageRepository.RemoveAsync(tenantId, userId, claims, new Dictionary<string, object>
            {
                { "Id", image.Id }
            });
        }

    }

    public Task<IEnumerable<Origin>> GetGlobalOriginsAsync(Guid tenantId)
    {
        return Task.FromResult(Mapper.Map<IEnumerable<Public.Origin>>(
            dbContext.Set<Persistables.Origin>()
                .Where(o => o.TenantId == tenantId && o.GlobalContext).ToList()));
    }
    
    public Task<IEnumerable<Origin>> GetQueryableOriginsAsync(Guid tenantId)
    {
        return Task.FromResult(Mapper.Map<IEnumerable<Public.Origin>>(
            dbContext.Set<Persistables.Origin>()
                .Where(o => o.TenantId == tenantId && !o.GlobalContext).ToList()));
    }
}
