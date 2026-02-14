using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;

namespace Ballware.Llm.Data.Ef.Repository;

public abstract class ChunkBaseRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Chunk, Persistables.Chunk>? hook = null) 
    : TenantableBaseRepository<Public.Chunk, Persistables.Chunk>(mapper, dbContext, hook)
{
    protected override IQueryable<Persistables.Chunk> ListQuery(IQueryable<Persistables.Chunk> query, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        if ("embedding".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            if (!queryParams.TryGetValue("embedding", out var embedding) || !Guid.TryParse(embedding.ToString(), out var embeddingId)) 
            {
                throw new ArgumentException("Embedding parameter is required");
            }

            return query.Where(er => er.EmbeddingId == embeddingId);
        }
        
        return base.ListQuery(query, identifier, claims, queryParams);
    }
}