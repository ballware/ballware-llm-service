using AutoMapper;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Ef.Repository;

public class EmbeddingBaseRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Embedding, Persistables.Embedding>? hook = null) 
    : TenantableRepository<Public.Embedding, Persistables.Embedding>(mapper, dbContext, hook), IEmbeddingRepository
{
    
}