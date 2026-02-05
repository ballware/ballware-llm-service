using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Repository;

public interface IEmbeddingRepository : ITenantableRepository<Public.Embedding>
{
    Task<IEnumerable<Public.Embedding>> GetActiveEmbeddingsForOrigin(Guid tenantId, Guid originId);
}