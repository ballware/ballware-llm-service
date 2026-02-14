using Ballware.Llm.Data.Ef.Repository;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Postgres.Repository;

public class ChunkRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Chunk, Persistables.Chunk>? hook = null) : ChunkBaseRepository(mapper, dbContext, hook), IChunkRepository
{
    
    
    public Task<IEnumerable<string>> FetchTopKSimilarChunksAsync(Guid tenantId, Guid embeddingId, float[] embeddingVector, int topK)
    {
        var queryVector = new Vector(embeddingVector);
        
        var result = dbContext.Set<Persistables.Chunk>()
            .Where(c => c.TenantId == tenantId && c.EmbeddingId == embeddingId)
            .OrderBy(c => c.Data.CosineDistance(queryVector))
            .Take(topK)
            .Select(c => c.Text)
            .ToList();
        
        return Task.FromResult(result as IEnumerable<string>);
    }

    public async Task RemoveByEmbeddingIdAsync(Guid tenantId, Guid embeddingId)
    {
        await dbContext.Set<Persistables.Chunk>()
            .Where(c => c.TenantId == tenantId && c.EmbeddingId == embeddingId)
            .ExecuteDeleteAsync();
        
        await dbContext.SaveChangesAsync();
    }
}