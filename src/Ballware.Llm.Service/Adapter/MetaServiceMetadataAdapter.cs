using AutoMapper;
using Ballware.Llm.Metadata;
using Ballware.Meta.Service.Client;
using Ballware.Shared.Authorization;

namespace Ballware.Llm.Service.Adapter;

public class MetaServiceMetadataAdapter : IAuthorizationMetadataProvider
{
    private IMapper Mapper { get; }
    private MetaServiceClient MetaClient { get; }
    
    public MetaServiceMetadataAdapter(IMapper mapper, MetaServiceClient metaClient)
    {
        Mapper = mapper;
        MetaClient = metaClient;
    }
    
    public async Task<ITenantAuthorizationMetadata?> MetadataForTenantByIdAsync(Guid tenantId)
    {
        var tenant = await MetaClient.TenantServiceMetadataAsync(tenantId);
        
        return Mapper.Map<Tenant>(tenant);   
    }

    public async Task<IEntityAuthorizationMetadata?> MetadataForEntityByTenantAndIdentifierAsync(Guid tenantId, string entity)
    {
        var entityMetadata = await MetaClient.EntityServiceMetadataForTenantByIdentifierAsync(tenantId, entity);
        
        return Mapper.Map<EntityMetadata>(entityMetadata);  
    }
}