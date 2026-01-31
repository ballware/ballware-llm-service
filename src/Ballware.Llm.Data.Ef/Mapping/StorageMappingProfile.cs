using AutoMapper;

namespace Ballware.Llm.Data.Ef.Mapping;

class StorageMappingProfile : Profile
{
    public StorageMappingProfile()
    {
        CreateMap<Public.Origin, Persistables.Origin>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => src.Id));
        
        CreateMap<Persistables.Origin, Public.Origin>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Uuid));
        
        CreateMap<Public.Embedding, Persistables.Embedding>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => src.Id));
        
        CreateMap<Persistables.Embedding, Public.Embedding>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Uuid));
        
        CreateMap<Public.Chunk, Persistables.Chunk>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => src.Id));
        
        CreateMap<Persistables.Chunk, Public.Chunk>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Uuid));
    }
}