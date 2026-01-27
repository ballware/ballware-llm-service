using AutoMapper;
using Ballware.Llm.Metadata;

namespace Ballware.Llm.Service.Mappings;

public class MetaServiceLlmMetadataProfile : Profile
{
    public MetaServiceLlmMetadataProfile()
    {
        CreateMap<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
            .ForMember(dst => dst.RightsCheckScript, 
                opt => opt.MapFrom(source => source.CustomScripts.ExtendedRightsCheck));
        
        CreateMap<Ballware.Meta.Service.Client.ServiceTenant, Tenant>();
    }
}