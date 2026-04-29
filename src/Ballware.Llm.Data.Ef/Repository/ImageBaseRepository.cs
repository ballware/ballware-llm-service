using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;
using MapsterMapper;

namespace Ballware.Llm.Data.Ef.Repository;

public abstract class ImageBaseRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Image, Persistables.Image>? hook = null)
    : TenantableBaseRepository<Public.Image, Persistables.Image>(mapper, dbContext, hook), IImageRepository
{
    protected override IQueryable<Persistables.Image> ListQuery(IQueryable<Persistables.Image> query, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        if ("origin".Equals(identifier, StringComparison.InvariantCultureIgnoreCase))
        {
            if (!queryParams.TryGetValue("origin", out var origin) || !Guid.TryParse(origin.ToString(), out var originId))
            {
                throw new ArgumentException("Origin parameter is required");
            }

            return query.Where(image => image.OriginId == originId);
        }

        return base.ListQuery(query, identifier, claims, queryParams);
    }

    public virtual Task<IEnumerable<Public.Image>> GetByOriginAsync(Guid tenantId, Guid originId)
    {
        return Task.FromResult(Mapper.Map<IEnumerable<Public.Image>>(
            dbContext.Set<Persistables.Image>()
                .Where(image => image.TenantId == tenantId && image.OriginId == originId)
                .ToList()));
    }

    public abstract Task<IEnumerable<Public.Image>> FetchTopKSimilarImagesAsync(Guid tenantId, Guid originId, float[] embeddingVector, int topK);
}
