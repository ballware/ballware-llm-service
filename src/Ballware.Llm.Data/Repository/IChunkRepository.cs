using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IChunkRepository : ITenantableRepository<Public.Chunk>
{
    Task<IEnumerable<string>> FetchTopKSimilarChunksAsync(Guid tenantId, Guid embeddingId, float[] embeddingVector, int topK);
    
    Task RemoveByEmbeddingIdAsync(Guid tenantId, Guid embeddingId);
}