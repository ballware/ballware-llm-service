using Ballware.Llm.Data.Ef.Repository;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Postgres.Repository;

public class ImageRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Image, Persistables.Image>? hook = null)
    : ImageBaseRepository(mapper, dbContext, hook), IImageRepository
{
    public override Task<IEnumerable<Public.Image>> FetchTopKSimilarImagesAsync(Guid tenantId, Guid originId, float[] embeddingVector, int topK)
    {
        var queryVector = new Vector(embeddingVector);

        var result = Mapper.Map<IEnumerable<Public.Image>>(
            dbContext.Set<Persistables.Image>()
                .Where(image => image.TenantId == tenantId && image.OriginId == originId)
                .OrderBy(image => image.Data.CosineDistance(queryVector))
                .Take(topK)
                .ToList());

        return Task.FromResult(result);
    }
}
