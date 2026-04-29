using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IImageRepository : ITenantableRepository<Public.Image>
{
    Task<IEnumerable<Public.Image>> GetByOriginAsync(Guid tenantId, Guid originId);
    Task<IEnumerable<Public.Image>> FetchTopKSimilarImagesAsync(Guid tenantId, Guid originId, float[] embeddingVector, int topK);
}
