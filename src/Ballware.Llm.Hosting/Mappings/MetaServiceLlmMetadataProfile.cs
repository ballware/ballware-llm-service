using Ballware.Llm.Metadata;
using Mapster;

namespace Ballware.Llm.Hosting.Mappings;

public class MetaServiceLlmMetadataProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
            .Map(dest => dest.RightsCheckScript, src => src.CustomScripts.ExtendedRightsCheck);

        config.NewConfig<Ballware.Meta.Service.Client.ServiceTenant, Tenant>();
    }
}
