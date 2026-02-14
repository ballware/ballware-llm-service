using Ballware.Llm.Data.Ef.Mapping;
using Mapster;

namespace Ballware.Llm.Data.Ef;

public static class MapperConfigurationExtensions
{
    public static TypeAdapterConfig AddBallwareLlmStorageMappings(
        this TypeAdapterConfig configuration)
    {
        new StorageMappingProfile().Register(configuration);
        
        return configuration;
    }
}