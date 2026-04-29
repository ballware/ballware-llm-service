using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ballware.Llm.Data.Public;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;

namespace Ballware.Llm.Data.Ef.Repository;

public class EmbeddingBaseRepository(IMapper mapper, ILlmDbContext dbContext, IChunkRepository chunkRepository, ITenantableRepositoryHook<Public.Embedding, Persistables.Embedding>? hook = null) 
    : TenantableBaseRepository<Public.Embedding, Persistables.Embedding>(mapper, dbContext, hook), IEmbeddingRepository
{
    protected override IQueryable<Persistables.Embedding> ListQuery(IQueryable<Persistables.Embedding> query, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        if ("origin".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            if (!queryParams.TryGetValue("origin", out var origin) || !Guid.TryParse(origin.ToString(), out var originId)) 
            {
                throw new ArgumentException("Origin parameter is required");
            }

            return query.Where(er => er.OriginId == originId);
        }
        
        return base.ListQuery(query, identifier, claims, queryParams);
    }
    
    protected override async Task BeforeRemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims, Persistables.Embedding persistable)
    {
        await base.BeforeRemoveAsync(tenantId, userId, claims, persistable);
        
        var chunks = (await chunkRepository.QueryAsync(tenantId, "embedding", claims, new Dictionary<string, object>
            {
                { "embedding", persistable.Uuid }
            })).ToList();

        foreach (var chunk in chunks)
        {
            await chunkRepository.RemoveAsync(tenantId, userId, claims, new Dictionary<string, object>
            {
                { "Id", chunk.Id }
            });
        }

    }

    public Task<IEnumerable<Embedding>> GetActiveEmbeddingsForOrigin(Guid tenantId, Guid originId)
    {
        return Task.FromResult(Mapper.Map<IEnumerable<Public.Embedding>>(
            dbContext.Set<Persistables.Embedding>()
                .Where(e => e.TenantId == tenantId && e.OriginId == originId && e.Active).ToList()
            ));
    }
}
