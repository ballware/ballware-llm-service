using AutoMapper;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Repository;

namespace Ballware.Llm.Data.Ef.Repository;

public class ChunkBaseRepository(IMapper mapper, ILlmDbContext dbContext, ITenantableRepositoryHook<Public.Chunk, Persistables.Chunk>? hook = null) 
    : TenantableBaseRepository<Public.Chunk, Persistables.Chunk>(mapper, dbContext, hook), IChunkRepository
{
    
}